using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public abstract class Session
	{
		private Socket _socket;
		private SocketAsyncEventArgs _recvArgs = new SocketAsyncEventArgs();
		private RingBuffer _recvBuffer;

		private const int HeaderSize = 2;

		public void Start(Socket socket, int recvBufferSize = 40)
		{
			_socket = socket;
			_recvArgs.Completed += OnRecvCompleted;
			_recvBuffer = new RingBuffer(recvBufferSize);
			Receive();
		}

		private void Receive()
		{
			// TODO : 임시로 버퍼 사이즈를 줄여서 테스트하는 중 버퍼가 가득차게 되면 stream을 처리하지 못하는 현상 발생으로 예외처리. 수정이 필요하다.
			if (_recvBuffer.FreeSize == 0)
			{
				Console.WriteLine( "Receive buffer is full. Closing Session." );
				close();
				return;
			}

			ArraySegment<byte> argsBuffer = _recvBuffer.WriteSegment();
			_recvArgs.SetBuffer(argsBuffer.Array, argsBuffer.Offset, argsBuffer.Count);

			try
			{
				bool pending = _socket.ReceiveAsync(_recvArgs);
				if(!pending)
					OnRecvCompleted( null, _recvArgs );
			}
			catch( Exception ex )
			{
				Console.WriteLine($"Receive Failed {ex}");
			}
			
		}

		private void OnRecvCompleted(object sender, SocketAsyncEventArgs args)
		{
			if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
			{
				try
				{
					// 수신에 따라 버퍼 사용량 체크
					_recvBuffer.CommitWrite( args.BytesTransferred );

					while(true)
					{
						// 최소 헤더 사이즈 확인
						if(_recvBuffer.DataSize < HeaderSize)
							break;

						byte[] readHeader = new byte[2];
						_recvBuffer.Peek( readHeader, 0, HeaderSize );
						ushort packetSize = BitConverter.ToUInt16(readHeader, 0);

						if(_recvBuffer.Capacity < packetSize)
						{
							Console.WriteLine( $"Packet Size ({packetSize}) exceeds buffer capacity. Closing session." );
							close();
							return;
						}

						// 패킷 전체 도착 확인
						if(_recvBuffer.DataSize < packetSize)
							break;

						byte[] data = new byte[packetSize];
						_recvBuffer.Read( data, 0, packetSize );

						OnRecvPacket( new ArraySegment<byte>( data ) );
					}

					Receive();
				}
				catch (Exception ex)
				{
					Console.WriteLine($"OnRecvComplated Failed : {ex}");
				}

				
			}
			else
			{
				close();
			}
		}

		public void Send( ushort PacketId, byte[] data)
		{
			ushort dataSize = (ushort)data.Length;
			ushort packetSize = (ushort)(dataSize + 4);

			byte[] sendBuffer = new byte[packetSize];
			Buffer.BlockCopy( BitConverter.GetBytes( packetSize ), 0, sendBuffer, 0, sizeof( ushort ) );
			Buffer.BlockCopy( BitConverter.GetBytes( PacketId ), 0, sendBuffer, 2, sizeof( ushort ) );
			Buffer.BlockCopy( data, 0, sendBuffer, 4, dataSize );

			try
			{
				_socket.Send( sendBuffer );
				OnSend( data.Length );
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Send Failed: {ex}");
				close();
			}
			
		}

		private void close()
		{
			if(_socket == null)
				return;

			OnDisConnected(_socket.RemoteEndPoint );
			_socket.Close();
			_socket = null;
		}

		public abstract void OnConnected( EndPoint endPoint );
		public abstract void OnRecvPacket(ArraySegment<byte> buffer);
		public abstract void OnSend( int bytes );
		public abstract void OnDisConnected(EndPoint endPoint );
	}
}
