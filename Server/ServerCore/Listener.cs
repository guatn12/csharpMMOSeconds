using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ServerCore
{
    public class Listener
    {
        private Socket _listenSocket;
        private Func<NetworkSession> _sessionFactory;
        private ILogger<Listener> _logger;
		private int _stopping;

        public Listener(ILogger<Listener> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public void Init( IPEndPoint endPoint, Func<NetworkSession> sessionFactory, int listenBacklog = 10 )
		{
			_sessionFactory = sessionFactory;
			_stopping = 0;
			_listenSocket = new Socket( endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp );
			_listenSocket.Bind( endPoint );
			_listenSocket.Listen( listenBacklog );

			for(int i = 0; i < 10; i++)
			{
				SocketAsyncEventArgs args = new SocketAsyncEventArgs();
				args.Completed += OnAcceptCompleted;
				RegisterAccept( args );
			}
		}

		public void Stop()
		{
			if(Interlocked.Exchange( ref _stopping, 1 ) != 0)
				return;

			Socket socket = Interlocked.Exchange(ref _listenSocket, null);
			if(socket == null)
				return;

			try
			{
				socket.Close();
			}
			catch(SocketException ex)
			{
				_logger.LogWarning( ex, "Listener close failed." );
			}
			finally
			{
				socket.Dispose();
			}
		}

		private void RegisterAccept(SocketAsyncEventArgs args)
        {
			if(Volatile.Read(ref _stopping) != 0)
			{
				args.Dispose();
				return;
			}

            args.AcceptSocket = null;
            try
            {
				Socket listenSocket = Volatile.Read(ref _listenSocket);
				if(listenSocket == null)
				{
					args.Dispose();
					return;
				}

				if(listenSocket.AcceptAsync( args ) == false)
					OnAcceptCompleted( null, args );
            }
			catch(ObjectDisposedException) when (Volatile.Read(ref _stopping) != 0)
			{
				args.Dispose();
			}
            catch (Exception e)
            {
                //LogManager.Error(e, "RegisterAccept failed.");
                _logger.LogError( e, "RegisterAccept failed." );
				args.Dispose();
            }
        }

        private void OnAcceptCompleted(object sender, SocketAsyncEventArgs args)
        {
			if(Volatile.Read(ref _stopping) != 0)
			{
				args.AcceptSocket?.Dispose();
				args.Dispose();
				return;
			}

            if (args.SocketError == SocketError.Success)
            {
                NetworkSession session = _sessionFactory.Invoke();
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
