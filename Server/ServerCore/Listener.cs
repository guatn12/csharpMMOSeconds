using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public class Listener
	{
		private Socket _listenSocket;
		private Func<Session> _sessionFactory;

		public void Init(IPEndPoint endPoint, Func<Session> sessionFactory, int listenBacklog = 10)
		{
			_sessionFactory = sessionFactory;
			_listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			_listenSocket.Bind(endPoint);
			_listenSocket.Listen( listenBacklog );

			for(int i = 0; i < 10; i++)
			{
				SocketAsyncEventArgs args = new SocketAsyncEventArgs();
				args.Completed += OnAcceptComplated;
				RegisterAccept( args );
			}
		}

		private void RegisterAccept(SocketAsyncEventArgs args)
		{
			args.AcceptSocket = null;

			bool pending = _listenSocket.AcceptAsync(args);
			if(pending == false)
			{
				OnAcceptComplated( null, args );
			}
		}

		private void OnAcceptComplated(object sender, SocketAsyncEventArgs args)
		{
			if(args.SocketError == SocketError.Success)
			{
				Session session = _sessionFactory.Invoke();
				session.Start(args.AcceptSocket);
				session.OnConnected( args.AcceptSocket.RemoteEndPoint );
			}

			RegisterAccept(args);
		}
	}
}
