using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public abstract class NetworkSession
	{
		private Socket _socket;

		private SocketAsyncEventArgs _recvArgs = new SocketAsyncEventArgs();
		private SocketAsyncEventArgs _sendArgs = new SocketAsyncEventArgs();
		private RingBuffer _recvBuffer;

		private readonly Queue<ArraySegment<byte>> _sendQueue = new Queue<ArraySegment<byte>>();
		private readonly object _lock = new object();
		private bool _isSending = false;
		private bool _isClosed = false;
		private readonly List<ArraySegment<byte>> _pendingList = new List<ArraySegment<byte>>();
		
		private const int HeaderSize = 2;

		public bool IsConnected()
		{
			lock (_lock)
			{
				try
				{
					return _socket != null && _socket.Connected && !(_socket.Poll( 1, SelectMode.SelectRead ) && _socket.Available == 0);
				}
				catch { return false; }
			}
		}

		public void Start( Socket socket, int recvBufferSize = 4096 )
		{
			_socket = socket;
			_recvArgs.Completed += OnRecvCompleted;

			_recvBuffer = new RingBuffer( recvBufferSize );
			Receive();
		}

		private void Receive()
		{
			// TODO : 임시로 버퍼 사이즈를 줄여서 테스트하는 중 버퍼가 가득차게 되면 stream을 처리하지 못하는 현상 발생으로 예외처리. 수정이 필요하다.
			if(_recvBuffer.FreeSize == 0)
			{
				Console.WriteLine( "Receive buffer is full. Closing Session." );
				Close();
				return;
			}

			ArraySegment<byte> argsBuffer = _recvBuffer.WriteSegment();
			_recvArgs.SetBuffer( argsBuffer.Array, argsBuffer.Offset, argsBuffer.Count );

			try
			{
				bool pending = _socket.ReceiveAsync(_recvArgs);
				if(!pending)
					OnRecvCompleted( null, _recvArgs );
			}
			catch(Exception ex)
			{
				Console.WriteLine( $"Receive Failed {ex}" );
			}

		}

		private void OnRecvCompleted( object sender, SocketAsyncEventArgs args )
		{
			if(args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
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
							Close();
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
				catch(Exception ex)
				{
					Console.WriteLine( $"OnRecvComplated Failed : {ex}" );
				}


			}
			else
			{
				Close();
			}
		}

		protected void Send( ArraySegment<byte> sendBuffer )
		{
			lock(_lock)
			{
				_sendQueue.Enqueue( sendBuffer );
				// 다른 스레드가 Sending을 진행 중인지 확인.
				if(!_isSending)
					ProcessSend();
			}
		}

		private void ProcessSend()
		{
			if(_socket == null || !_socket.Connected)
			{
				Close();
				return;
			}

			lock(_lock)
			{
				if(_isSending)
					return;

				_pendingList.Clear();

				while(0 < _sendQueue.Count)
				{
					ArraySegment<byte> segment = _sendQueue.Dequeue();
					_pendingList.Add( segment );
				}

				if(_pendingList.Count == 0)
					return;

				_sendArgs.BufferList = _pendingList;
				_isSending = true;
			}

			try
			{
				bool pending = _socket.SendAsync(_sendArgs);
				if(!pending)
					OnSendCompleted(null, _sendArgs);
			}
			catch(Exception ex)
			{
				Console.WriteLine( $"Send Failed: {ex}" );
			}
		}

		private void OnSendCompleted(object sender, SocketAsyncEventArgs args)
		{
			lock(_lock)
			{
				if( 0 < args.BytesTransferred || args.SocketError == SocketError.Success)
				{
					try
					{
						_sendArgs.BufferList = null;
						_pendingList.Clear();

						OnSend( args.BytesTransferred );

						if(0 < _sendQueue.Count)
						{
							ProcessSend();
						}
						else
						{
							_isSending = false;
						}
					}
					catch(Exception ex)
					{
						Console.WriteLine($"OnSendCompleted Failed: {ex}");
						Close();
					}
				}
				else
				{
					Close();
				}
			}
		}

		public void Close()
		{
			lock( _lock )
			{
				if( _isClosed || _socket == null)
					return;

				_isClosed = true;

				try
				{
					OnDisConnected( _socket.RemoteEndPoint );
					_socket.Shutdown( SocketShutdown.Both );
				}
				catch(Exception ex)
				{
					// 이미 종료된 오류는 무시.
				}

				_socket.Close();
			}
			
			Clear();
		}

		private void Clear()
		{
			lock( _lock)
			{
				_pendingList.Clear();
				_sendQueue.Clear();
			}
			_socket = null;
		}

		public abstract void OnConnected( EndPoint endPoint );
		public abstract void OnRecvPacket( ArraySegment<byte> buffer );
		public abstract void OnSend( int bytes );
		public abstract void OnDisConnected( EndPoint endPoint );
	}
}
