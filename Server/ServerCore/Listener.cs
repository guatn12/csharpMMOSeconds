using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Sockets;

namespace ServerCore
{
    public class Listener
    {
        private Socket _listenSocket;
        private Func<Session> _sessionFactory;
        private ILogger<Listener> _logger;

        public Listener(ILogger<Listener> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Init(IPEndPoint endPoint, Func<Session> sessionFactory, int listenBacklog = 10)
        {
            _sessionFactory = sessionFactory;
            _listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listenSocket.Bind(endPoint);
            _listenSocket.Listen(listenBacklog);

            for (int i = 0; i < 10; i++)
            {
                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.Completed += OnAcceptCompleted;
                RegisterAccept(args);
            }
        }

        private void RegisterAccept(SocketAsyncEventArgs args)
        {
            args.AcceptSocket = null;

            try
            {
                bool pending = _listenSocket.AcceptAsync(args);
                if (pending == false)
                {
                    OnAcceptCompleted(null, args);
                }
            }
            catch (Exception e)
            {
                //LogManager.Error(e, "RegisterAccept failed.");
                _logger.LogError( e, "RegisterAccept failed." );
            }
        }

        private void OnAcceptCompleted(object sender, SocketAsyncEventArgs args)
        {
            if (args.SocketError == SocketError.Success)
            {
                Session session = _sessionFactory.Invoke();
				session.OnConnected( args.AcceptSocket.RemoteEndPoint );
				session.Start(args.AcceptSocket);
            }
            else
            {
                //LogManager.Error(null, "Accept failed with SocketError: {SocketError}", args.SocketError);
                _logger.LogError( "Accept failed with SocketError: {SocketError}", args.SocketError );
            }
            
            RegisterAccept(args);
        }
    }
}
