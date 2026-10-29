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
using Protocol;
using System.Threading;

namespace DummyClient
{
	public enum HandshakeResult { Success, Rejected, Disconnected }

	public class ServerSession : NetworkSession
	{
		public int DummyId { get; private set; }
		public ClientContext Context { get; set; }
		//private readonly ILogger<ServerSession> _logger;
		public long SessionId { get; private set; }

		private static long _nextSessionId = 1;
		private string _loginTicket;

		private readonly PacketManager _packetManager;

		private readonly TaskCompletionSource<HandshakeResult> _loginCompletion =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		private readonly TaskCompletionSource<HandshakeResult> _entryCompletion =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		private readonly TaskCompletionSource<bool> _disconnected =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<HandshakeResult> LoginCompletion => _loginCompletion.Task;
		public Task<HandshakeResult> EntryCompletion => _entryCompletion.Task;
		public Task Disconnected => _disconnected.Task;

		public void CompleteLogin( bool success ) =>
			_loginCompletion.TrySetResult( success ? HandshakeResult.Success : HandshakeResult.Rejected );

		public void CompleteEntry( bool success ) =>
			_entryCompletion.TrySetResult( success ? HandshakeResult.Success : HandshakeResult.Rejected );

		private static long GenerateNextSessionId()
		{
			return System.Threading.Interlocked.Increment( ref _nextSessionId );
		}

		public ServerSession(ILogger<ServerSession> logger, PacketManager packetManager, string loginTicket )
			: base( logger )
		{
			//_logger = logger;
			_packetManager = packetManager;
			_loginTicket = loginTicket ?? throw new ArgumentNullException( nameof(loginTicket) );
		}

		public void Send( IMessage packet )
		{
			ArraySegment<byte> segment = _packetManager.MakeSendPacket( packet );

			base.Send( segment );
		}

		public override void OnConnected( EndPoint endPoint )
		{
			SessionId = GenerateNextSessionId();
			string ticket = Interlocked.Exchange( ref _loginTicket, null );
			if(string.IsNullOrEmpty( ticket ))
			{
				Close();
				return;
			}
			_logger.LogInformation( "OnConnected: {endPoint}", endPoint );

			// C_Login 패킷 전송
			C_Login loginPacket = new C_Login {Token = ticket };
			Send( loginPacket );
			_logger.LogInformation( "C_Login 패킷 전송 완료" );
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			Interlocked.Exchange( ref _loginTicket, null );
			_loginCompletion.TrySetResult( HandshakeResult.Disconnected );
			_entryCompletion.TrySetResult( HandshakeResult.Disconnected );
			_disconnected.TrySetResult( true );
			_logger.LogInformation( "OnDisConnected: {endPoint}, SessionId={SessionId}", endPoint, SessionId );

			// ClientContext 정리 - MainLoop의 다음 IsConnected 검사 전 상태 일관성 확보
			if(Context != null)
			{
				Context.MyPlayer.Clear();
				Context.CurrentMapData = null;
				Context.NearbyObjects.Clear();
				Context.TargetMonsterId = 0;
				Context.AutoMoveWaypoints.Clear();
				Context.InventoryRequested = false;
				Context.SkillCooldowns.Clear();
			}
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
