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
		public long SessionId { get; private set; }

		private static long _nextSessionId = 1;

		private readonly PacketManager _packetManager;

		private static long GenerateNextSessionId()
		{
			return System.Threading.Interlocked.Increment( ref _nextSessionId );
		}

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
			SessionId = GenerateNextSessionId();
			_logger.LogInformation( "OnConnected: {endPoint}", endPoint );

			// C_EnterGame 패킷 전송
			Protocol.C_EnterGame enterGamePacket = new Protocol.C_EnterGame();
			Send(enterGamePacket);
			_logger.LogInformation("C_EnterGame 패킷 전송 완료");
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			_logger.LogInformation( "OnDisConnected: {endPoint}", endPoint );
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
