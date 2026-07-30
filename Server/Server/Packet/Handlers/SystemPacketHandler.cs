using DatabaseLib.Entities;
using DatabaseLib.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Protocol;
using Server.Config;
using Server.Core.Session;
using Server.Data;
using Server.Room;
using Server.Services;
using Server.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
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
		private readonly IRoomTransitionCoordinator _transitionCoordinator;
		private readonly IGameDataService _gameDataService;
		private readonly IRedisService _redisService;
		private readonly ISessionManager _sessionManager;
		private readonly IDataManager _dataManager;

		public SystemPacketHandler(ILogger<SystemPacketHandler> logger, IRoomManager roomManager, IOptions<ServerSettings> settings, 
			IRoomTransitionCoordinator transitionCoordinator, IGameDataService gameDataService, IRedisService redisService, ISessionManager sessionManager,
			IDataManager dataManager)
		{
			_logger = logger;
			_roomManager = roomManager;
			_serverSettings = settings.Value;
			_transitionCoordinator = transitionCoordinator;
			_gameDataService = gameDataService;
			_redisService = redisService;
			_sessionManager = sessionManager;
			_dataManager = dataManager;
			InitializeHandlers();
		}
		private async ValueTask HandleC_LoginAsync( IClientSession session, C_Login packet )
		{
			// connected -> authenticating 전이
			if(session.TryTransitionTo( SessionState.Authenticating ) == false)
				return; // 이미 인증 중 또는 다른 상태

			// 세션에 플레이어가 있는지 여부를 통해 이미 로그인 상태인지 체크
			if(session.Player != null)
			{
				session.TryTransitionTo( SessionState.Connected );  // 상태 복구
				session.Send( new S_Login { Success = false, Message = "이미 로그인 상태" } );
				return;
			}

			string tokenId = await _redisService.GetStringAsync(RedisKeys.TokenUserId(packet.Token));
			if(string.IsNullOrEmpty( tokenId ))
			{
				session.TryTransitionTo( SessionState.Connected );      // 상태 복구 (Disconnect 직전에라도 일관성)
				session.Send( new S_Login { Success = false, Message = "인증 실패" } );
				session.Disconnect();
				return;
			}

			long accountId = long.Parse(tokenId);
			

			List<PlayerEntity> players = await _gameDataService.GetPlayerListByAccountIdAsync(accountId);

			session.TryTransitionTo( SessionState.Authenticated );

			// 로그인 완료 시 session token 변경
			session.SetLoginToken( packet.Token );
			session.BindAccountId( accountId );

			var response = new S_Login { Success = true, AccountId = accountId };
			response.Players.AddRange( players.Select( p => new PlayerInfo
			{
				PlayerId = p.PlayerId,
				PlayerName = p.PlayerName,
				Level = p.Level,
				CreatedAt = ((DateTimeOffset)p.CreatedAt).ToUnixTimeSeconds()
			} ) );
			
			session.Send( response );

			_logger.LogInformation( "로그인 성공: sessionId={SessionId}, accountId={accountId}",
				session.SessionId, accountId );
		}

		private async Task HandleC_EnterGameAsync( IClientSession session, C_EnterGame packet)
		{
			if(session.TryTransitionTo( SessionState.EnteringGame ) == false)
				return; // 이미 입장 중 또는 다른 상태

			long playerId = packet.PlayerId;

			// 계정 소유 캐릭터인지 검증
			bool isOwned = await _gameDataService.IsPlayerOwnedByAccountAsync(session.AccountId, playerId);
			if(isOwned == false)
			{
				_logger.LogWarning( "타인 캐릭터 진입 시도: AccountId={AccountId}, PlayerId={PlayerId}",
					session.AccountId, playerId );
				session.TryTransitionTo( SessionState.Authenticated );
				session.Disconnect();
				return;
			}

			// 데이터 로드
			var aggregate = await _gameDataService.LoadPlayerAggregateAsync(session.AccountId, playerId);
			if(aggregate == null )
			{
				_logger.LogError( "캐릭터 데이터 로드 실패: PlayerID={PlayerId}", playerId );
				session.TryTransitionTo( SessionState.Authenticated );
				return;
			}

			// 플레이어 생성 -> 로드 데이터 적용 -> 바인딩
			session.CreatePlayer( _dataManager, playerId, aggregate.Player.PlayerName );
			var missingEquipInstances = session.Player.ApplyLoadedData( aggregate.Player, aggregate.Inventory, aggregate.Equipment );
			if( 0 < missingEquipInstances.Count )
			{
				_logger.LogWarning( "장비 참조 복구: PlayerId = {PlayerId}, MissingEquipInstances={missingEquipInstances}",
					playerId, string.Join( ",", missingEquipInstances ) );
			}
			_sessionManager.BindPlayerToSession( session.SessionId );

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
				session.TryTransitionTo(SessionState.Authenticated); // 상태 복구
				return;
			}

			session.TryTransitionTo( SessionState.InRoom );
			session.CurrentRoom.SendToPlayer( session, new S_EnterGame()
			{
				Player = session.Player.ToObjectInfo(),
				MapId = session.CurrentRoom.RoomMap.MapId,
			} );
		}

		private async Task HandleC_ChangeRoomAsync(IClientSession session, C_ChangeRoom packet)
		{
			if(session.TryTransitionTo( SessionState.Transferring ) == false)
				return;

			if(session.CurrentRoom == null)
			{
				_logger.LogError( "C_ChangeRoom rejected - session.CurrentRoom is null. SessionId={SessionId}", session.SessionId );
				session.Disconnect(); // 심각한 오류로 간주하여 연결 종료
				return;
			}

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
				session.TryTransitionTo( SessionState.InRoom );
				session.Send( new S_ChangeRoom
				{
					Success = false,
					FailReason = "입장 가능한 방이 없습니다."
				} );
				return;
			}

			// Coordinator 위임
			RoomTransitionResult result = await _transitionCoordinator.ChangeRoomAsync(session, targetRoom.RoomId, RoomTransitionReason.PlayerRequest);

			// 성공 / 실패 여부와 관계없이 복귀
			session.TryTransitionTo( SessionState.InRoom );

			if(result != RoomTransitionResult.Success)
			{
				string failReason = MapTransitionResultToFailReason(result);

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

		private async ValueTask HandleC_CreatePlayerAsync( IClientSession session, C_CreatePlayer packet )
		{
			long accountId = session.AccountId;

			// 1. 이름 검증(길이 / 형식 / 금지어)
			var validation = PacketValidators.ValidatePlayerName(packet.PlayerName);
			if(validation.IsValid == false)
			{
				session.Send( new S_CreatePlayer { Success = false, FailReason = validation.ErrorMessage } );
				return;
			}

			// 2. 이름 중복 검사
			if(await _gameDataService.IsPlayerNameTakenAsync(packet.PlayerName))
			{
				session.Send( new S_CreatePlayer { Success = false, FailReason = "이미 사용중인 이름입니다." } );
				return;
			}

			// 3. 생성 - playerId는 db - identity 발급 / inventory/equipment row 동시 생성
			var aggregate = await _gameDataService.CreateNewPlayerAsync(packet.PlayerName, session.AccountId);
			if(aggregate == null)
			{
				session.Send( new S_CreatePlayer { Success = false, FailReason = "캐릭터 생성 실패 (재시도 필요)" } );
				return;
			}

			session.Send( new S_CreatePlayer
			{
				Success = true,
				Player = new PlayerInfo
				{
					PlayerId = aggregate.Player.PlayerId,
					PlayerName = aggregate.Player.PlayerName,
					Level = aggregate.Player.Level,
					CreatedAt = ((DateTimeOffset)aggregate.Player.CreatedAt).ToUnixTimeSeconds(),
				}
			} );


			_logger.LogInformation( "캐릭터 생성: AccountId={AccountId}, PlayerId={PlayerId}, Name={Name}", accountId, aggregate.Player.PlayerId, packet.PlayerName );
		}

		private Task HandleC_PingAsync(IClientSession session, C_Ping packet)
		{
			// 클라이언트로 부터 PING을 받았을 때 처리하는 패킷
			session.Send( new S_Pong
			{
				Timestamp = Environment.TickCount64
			} );
			_logger.LogInformation( "Received PING from Player {PlayerId} (Session {SessionId}). Responded with PONG.",
				session.PlayerId, session.SessionId );

			return Task.CompletedTask;
		}

		private string MapTransitionResultToFailReason( RoomTransitionResult result )
		{
			return result switch
			{
				RoomTransitionResult.TargetFull => "방이 가득 찼습니다.",
				RoomTransitionResult.TargetClosed => "방이 닫혀 있습니다.",
				RoomTransitionResult.TargetNotFound => "방을 찾을 수 없습니다.",
				RoomTransitionResult.AlreadyTransferring => "이미 방 이동 중입니다.",
				RoomTransitionResult.Cancelled => "방 이동이 취소되었습니다.",
				RoomTransitionResult.RollbackSucceeded => "방 이동에 실패했습니다. 원래 방으로 복귀했습니다.",
				RoomTransitionResult.RollbackFailed => "방 이동에 실패했습니다.",
				_ => "방 이동에 실패했습니다."
			};
		}
	}
}
