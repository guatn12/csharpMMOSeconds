using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using DummyClient.Packet;

namespace DummyClient
{
	public class ServerSession : Session
	{
		public int DummyId { get; private set; }
		private readonly ILogger<ServerSession> _logger;
		public long DummySessionId { get; set; }

		private readonly PacketManager _packetManager;

		public ServerSession(ILogger<ServerSession> logger, PacketManager packetManager )
		{
			_logger = logger;
			_packetManager = packetManager;
		}

		public void Send( IMessage packet )
		{
			ArraySegment<byte> segment = _packetManager.MakeSendPacket( packet );

			base.Send( segment );
		}

		public override void OnConnected( EndPoint endPoint )
		{
			Program.Session = this;
			//Console.WriteLine( $"OnConnected: {endPoint}" );
			_logger.LogInformation( "OnConnected: {endPoint}", endPoint );
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			//Console.WriteLine( $"OnDisConnected: {endPoint}" );
			_logger.LogInformation( "OnDisConnected: {endPoint}", endPoint );
			Program.Session = null;
		}

		public override void OnRecvPacket( ArraySegment<byte> buffer )
		{
			// 서버 패킷 처리.
			_ = _packetManager.HandlePacket( this, buffer );
		}

		public override void OnSend( int bytes )
		{
			
		}
	}
}
