using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Protocol;
using Server.Config;
using Server.Core.Session;
using Server.Room;
using System;
using System.Threading.Tasks;

namespace Server.Packet.Handlers
{
	/// <summary>
	/// 시스템 관련 패킷 핸들러
	/// </summary>
	public partial class SystemPacketHandler
	{
		private readonly ILogger<SystemPacketHandler> _logger;
		private readonly IRoomManager _roomManager;
		private readonly ServerSettings _serverSettings;

		public SystemPacketHandler(ILogger<SystemPacketHandler> logger, IRoomManager roomManager, IOptions<ServerSettings> settings )
		{
			_logger = logger;
			_roomManager = roomManager;
			_serverSettings = settings.Value;
			InitializeHandlers();
		}

		private async Task HandleC_EnterGameAsync( IClientSession session, C_EnterGame packet)
		{
			if(session.TryTransitionTo( SessionState.EnteringGame ) == false)
				return; // 이미 입장 중 또는 다른 상태


			// 자동 로비 입장
			var result = await _roomManager.JoinDefaultLobbyAsync( session );
			if(result == RoomEnterResult.Success)
			{
				_logger.LogInformation( "Player {PlayerId} (Session {SessionId}) automatically joined the default lobby.",
					session.Player.ObjectId, session.SessionId );
			}
			else
			{
				// TODO : 기본 로비 입장 실패 시 로비 생성 및 입장 처리가 필요.
				_logger.LogWarning( "Player {PlayerId} (Session {SessionId}) failed to join the default lobby.",
					session.Player.ObjectId, session.SessionId );
				session.TryTransitionTo(SessionState.Connected); // 상태 복구
				return;
			}

			session.TryTransitionTo( SessionState.InRoom );
			session.CurrentRoom.SendToPlayer( session, new S_EnterGame()
			{
				Player = session.Player.ToObjectInfo(),
				MapId = session.CurrentRoom.RoomMap.MapId,
			} );

			await Task.CompletedTask;
		}

		private async Task HandleC_ChangeRoomAsync(IClientSession session, C_ChangeRoom packet)
		{
			if(session.TryTransitionTo( SessionState.Transferring ) == false)
				return;

			var targetRoomType = (RoomType)packet.RoomType;
			var maxPlayers = _serverSettings.Room.Lobby.MaxPlayers;

			switch(targetRoomType)
			{
			case RoomType.Lobby:
				maxPlayers = _serverSettings.Room.Lobby.MaxPlayers; break;
			case RoomType.Battle:
				maxPlayers = _serverSettings.Room.Battle.MaxPlayers; break;
			case RoomType.Dungeon:
				maxPlayers = _serverSettings.Room.Dungeon.MaxPlayers; break;
			case RoomType.Private:
				maxPlayers = _serverSettings.Room.Private.MaxPlayers; break;
			case RoomType.Guild:
				maxPlayers = _serverSettings.Room.Guild.MaxPlayers; break;
			}

			// 이미 같은 타입의 방에 있으면 차단
			if(session.CurrentRoom.RoomType == targetRoomType)
			{
				session.TryTransitionTo( SessionState.InRoom ); // 상태 복구
				session.Send( new S_ChangeRoom
				{
					Success = false,
					FailReason = $"이미 {targetRoomType}방에 있습니다."
				} );
				return;
			}

			// 가용한 방 검색
			IRoom targetRoom = await _roomManager.FindAvailableRoomAsync(targetRoomType);

			// 방이 없으면 On-Deamand 생성 시도
			if(targetRoom == null)
			{
				targetRoom = await _roomManager.CreateRoomAsync( targetRoomType, $"{targetRoomType}-{packet.TargetId}",
					maxPlayers );
			}

			if(targetRoom == null)
			{
				session.Send( new S_ChangeRoom
				{
					Success = false,
					FailReason = "입장 가능한 방이 없습니다."
				} );
				return;
			}

			// 방 이동(현재 방 퇴장 + 대상 방 입장)
			RoomEnterResult result = await _roomManager.MovePlayerToRoomAsync(session, targetRoom.RoomId);

			// 성공 / 실패 여부와 관계없이 복귀
			session.TryTransitionTo( SessionState.InRoom );

			if(result != RoomEnterResult.Success)
			{
				string failReason = result switch
				{
					RoomEnterResult.RoomFull => "방이 가득 찼습니다.",
					RoomEnterResult.RoomClosed => "방이 닫혀 있습니다.",
					_ => "방 이동에 실패했습니다."
				};

				session.Send( new S_ChangeRoom
				{
					Success= false,
					FailReason = failReason
				} );
				return;
			}

			// 성공 - S_ChangeRoom 하나로 결과 + 입장 정보 전달
			session.Send( new S_ChangeRoom
			{
				Success = true,
				Player = session.Player.ToObjectInfo(),
				MapId = session.CurrentRoom.RoomMap.MapId
			} );

			_logger.LogInformation( "Player {PlayerId} moved to {RoomType} room {RoomId} (mapId={MapId})",
				session.PlayerId, targetRoomType, targetRoom.RoomId, session.CurrentRoom.RoomMap.MapId );
		}

		private Task HandleC_PingAsync(IClientSession session, C_Ping packet)
		{
			// 클라이언트로 부터 PING을 받았을 때 처리하는 패킷
			session.Send( new S_Pong
			{
				Timestamp = Environment.TickCount64
			} );
			_logger.LogInformation( "Received PING from Player {PlayerId} (Session {SessionId}). Responded with PONG.",
				session.Player.ObjectId, session.SessionId );

			return Task.CompletedTask;
		}
	}
}
