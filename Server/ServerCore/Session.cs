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
		private byte[] _ioBuffer = new byte[4096];

		// TODO : 일단 쉬운 리스트 버퍼로 추가. - 링 버퍼로 고도화 예정.
		private List<byte> _recvBuffer = new List<byte>();

		public void Start(Socket socket)
		{
			_socket = socket;
			_recvArgs.Completed += OnRecvCompleted;
			// i/o 버퍼 등록.
			_recvArgs.SetBuffer(_ioBuffer, 0, _ioBuffer.Length);
			Receive();
		}

		private void Receive()
		{
			bool pending = _socket.ReceiveAsync(_recvArgs);
			if(!pending)
				OnRecvCompleted( null, _recvArgs );
		}

		private void OnRecvCompleted(object sender, SocketAsyncEventArgs args)
		{
			if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
			{
				// 수신 버퍼에 데이터 추가.
				_recvBuffer.AddRange(new ArraySegment<byte>(args.Buffer, args.Offset, args.BytesTransferred));

				while(true)
				{
					// 최소 헤더 사이즈 확인
					if(_recvBuffer.Count <4)
						break;

					byte[] sizeBytes = _recvBuffer.GetRange(0, 2).ToArray();
					ushort packetSize = BitConverter.ToUInt16(sizeBytes, 0);

					// 패킷 전체 도착 확인
					if(_recvBuffer.Count < packetSize)
						break;

					ArraySegment<byte> packet = new ArraySegment<byte>(_recvBuffer.GetRange(0, packetSize).ToArray());
					OnRecvPacket( packet );

					_recvBuffer.RemoveRange( 0, packetSize );
				}

				Receive();
			}
			else
			{
				OnDisConnected( _socket.RemoteEndPoint );
				_socket.Close();
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

			_socket.Send( sendBuffer );
			OnSend( data.Length );
		}

		public abstract void OnConnected( EndPoint endPoint );
		public abstract void OnRecvPacket(ArraySegment<byte> buffer);
		public abstract void OnSend( int bytes );
		public abstract void OnDisConnected(EndPoint endPoint );
	}
}
