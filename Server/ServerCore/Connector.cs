using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public class Connector
	{
		private Func<Session> _sessionFactory;

		public void Connect(IPEndPoint endPoint, Func<Session> sessionFactory)
		{
			_sessionFactory = sessionFactory;
			Socket socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

			SocketAsyncEventArgs args = new SocketAsyncEventArgs();
			args.Completed += OnConnectCompleted;
			args.RemoteEndPoint = endPoint;
			args.UserToken = socket;

			RegisterConnect( args );
		}

		private void RegisterConnect( SocketAsyncEventArgs args )
		{
			Socket socket = args.UserToken as Socket;
			if(socket == null)
				return;

			bool pending = socket.ConnectAsync( args );
			if(!pending)
				OnConnectCompleted( null, args );
		}

		private void OnConnectCompleted( object sender, SocketAsyncEventArgs args )
		{
			if(args.SocketError == SocketError.Success)
			{
				Session session = _sessionFactory.Invoke();
				session.Start( args.ConnectSocket );
				session.OnConnected(args.RemoteEndPoint );
			}
			else
			{
				Console.WriteLine($"OnConnectedCompleted Fail: {args.SocketError}");
			}
		}
	}
}
