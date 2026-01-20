using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ServerCore
{
	public class Connector
	{
		private Func<NetworkSession> _sessionFactory;
		public ManualResetEvent ConnectDone { get; private set; }
		public bool IsConnected { get; private set; }
		public SocketError LastError { get; private set; }
		public NetworkSession ConnectionSession { get; private set; }

		public void Connect(IPEndPoint endPoint, Func<NetworkSession> sessionFactory)
		{
			_sessionFactory = sessionFactory;
			ConnectDone = new ManualResetEvent(false);
			IsConnected = false;
			LastError = SocketError.Success;

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
			{
				IsConnected = false;
				LastError = SocketError.SocketError;
				ConnectDone.Set();
				return;
			}

			bool pending = socket.ConnectAsync( args );
			if(!pending)
				OnConnectCompleted( null, args );
		}

		private void OnConnectCompleted( object sender, SocketAsyncEventArgs args )
		{
			if(args.SocketError == SocketError.Success)
			{
				NetworkSession session = _sessionFactory.Invoke();
				session.Start( args.ConnectSocket );
				session.OnConnected(args.RemoteEndPoint );

				ConnectionSession = session;
				IsConnected = true;
				LastError = SocketError.Success;
			}
			else
			{
				Console.WriteLine($"OnConnectedCompleted Fail: {args.SocketError}");

				IsConnected= false;
				LastError = args.SocketError;
			}

			ConnectDone.Set();
		}
	}
}
