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
		private byte[] _buffer = new byte[1024];

		public void Start(Socket socket)
		{
			_socket = socket;
			_recvArgs.Completed += OnRecvCompleted;
			_recvArgs.SetBuffer(_buffer, 0, _buffer.Length);
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
				OnRecv( new ArraySegment<byte>( args.Buffer, args.Offset, args.BytesTransferred ) );
				Receive();
			}
			else
			{
				OnDisConnected( _socket.RemoteEndPoint );
				_socket.Close();
			}
		}

		public void Send( byte[] data)
		{
			_socket.Send( data );
			OnSend( data.Length );
		}

		public abstract void OnConnected( EndPoint endPoint );
		public abstract void OnRecv(ArraySegment<byte> buffer);
		public abstract void OnSend( int bytes );
		public abstract void OnDisConnected(EndPoint endPoint );
	}
}
