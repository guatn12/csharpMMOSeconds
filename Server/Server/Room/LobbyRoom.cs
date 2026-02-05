using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Protocol;
using Server.Config;
using Server.Core.Session;
using Server.Data;
using Server.Game.Monsters;
using Server.Services;
using Server.Services.Combat;
using Server.Services.Reward;
using ServerCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Room
{
	public class LobbyRoom : BaseRoom
	{
		private readonly ServerSettings _serverSettings;
		private readonly ILogger<LobbyRoom> _lobbyLogger;
		private DateTime _createdAt;
		private int _totalVisitors = 0;

		public override RoomType RoomType => RoomType.Lobby;

		public DateTime CreateAt => _createdAt;
		public int TotalVisitors => _totalVisitors;
		// 로비가 기본 로비인지 여부
		public bool IsDefaultLobby { get; private set; }

		public LobbyRoom( ILogger<LobbyRoom> logger, ILoggerFactory loggerFactory, IOptions<ServerSettings> ServerSettings,
			DataManager datamanager, IJobQueueManager jobQueueManager, ICombatService combatService, IRewardService rewardService,
			PlayerPositionService playerPositionService,
			string roomName = null, bool isDefaultLobby = false ) 
			: base( logger, loggerFactory, roomName ?? "Main Lobby", ServerSettings.Value.Room.Lobby.MaxPlayers, datamanager,
				  jobQueueManager, combatService, rewardService, playerPositionService)
		{
			_lobbyLogger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			_serverSettings = ServerSettings.Value ?? throw new ArgumentNullException(nameof( ServerSettings ) );
			IsDefaultLobby = isDefaultLobby;
			_createdAt = DateTime.UtcNow;

			_lobbyLogger.LogInformation( "LobbyRoom Created: '{RoomName}' (ID: {RoomId}, Default: {IsDefault}, MaxPlayers: {MaxPlayers})",
				RoomName, RoomId, IsDefaultLobby, MaxPlayers );
		}

		protected override async Task OnInitializeAsync()
		{
			await base.OnInitializeAsync();

			_lobbyLogger.LogInformation( "LobbyRoom '{RoomName}' (ID: {RoomId}) initialized successfully",
				RoomName, RoomId);

			// 로비 특화 초기화 로직 (필요시)
			await SetUpLobbyEnvironmentAsync();
		}

		protected override async Task OnPlayerEnterAsync(IClientSession session)
		{
			// 방문자 수 증가
			Interlocked.Increment( ref _totalVisitors );

			_lobbyLogger.LogInformation( "Player {SessionId} entered lobby '{RoomName}' (Total visitors: {TotalVisitors})",
				 session.SessionId, RoomName, _totalVisitors );

			// 로비 스폰 위치로 플레이어 이동
			await SetPlayerSpawnPositionAsync( session );

			// 입장한 플레이어에게 메시지 전송
			await SendWelcomeMessageAsync( session );

			// 다른 플레이어들에게 입장 알림
			await NotifyPlayerJoinedAsync( session );

			// 로비 상태 정보 전송
			await SendLobbyStatusAsync( session );

			await base.OnPlayerEnterAsync( session );
		}

		protected override async Task OnPlayerLeaveAsync(IClientSession session)
		{
			_lobbyLogger.LogInformation( "Player {SessionId} left lobby '{RoomName}' ({CurrentCount}/{MaxPlayers})",
				  session.SessionId, RoomName, CurrentPlayerCount, MaxPlayers );

			// 다른 플레이어에게 퇴장 알림
			await NotifyPlayerLeftAsync( session );

			await base.OnPlayerLeaveAsync( session );
		}

		public override async Task OnPlayerMoveAsync(IClientSession session, Protocol.C_Move packet)
		{
			// 로비에서는 기본 이동만 허용 (특별한 제약 없음)
			_lobbyLogger.LogDebug( "Player {SessionId} moved in lobby '{RoomName}' to ({X}, {Y}, {Z})",
				 session.SessionId, RoomName, packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ );

			await base.OnPlayerMoveAsync( session, packet );
		}

		public override async Task OnPlayerChatAsync(IClientSession session, Protocol.C_Chat packet)
		{
			// 로비 채팅 로그
			_lobbyLogger.LogInformation( "Lobby chat from Player {SessionId} in '{RoomName}': '{Message}'",
				session.SessionId, RoomName, packet.Message );

			await base.OnPlayerChatAsync( session, packet );
		}

		public override async Task<bool> ValidatePlayerMoveAsync(IClientSession session, Protocol.C_Move packet)
		{
			// 로비에서는 모든 이동 허용 (기본 검증만)
			if (packet?.PosInfo == null)
			{
				_lobbyLogger.LogWarning( "Invalid move packet from Player {SessionId} in lobby '{RoomName}'",
					  session.SessionId, RoomName );
				return false;
			}

			// 기본 범위 검증
			PosInfo pos = packet.PosInfo;
			if(1000 < Math.Abs(pos.PosX) || 1000 < Math.Abs(pos.PosY) || 1000 < Math.Abs(pos.PosZ))
			{
				_lobbyLogger.LogWarning( "Player {SessionId} attempted to move out of bounds in lobby '{RoomName}': ({X}, {Y}, {Z})",
					  session.SessionId, RoomName, pos.PosX, pos.PosY, pos.PosZ );
				return false;
			}

			return await base.ValidatePlayerMoveAsync( session, packet);
		}

		public override async Task<bool> ValidatePlayerChatAsync(IClientSession session, Protocol.C_Chat packet)
		{
			// 로비 채팅 검증
			if(string.IsNullOrWhiteSpace( packet?.Message ))
				return false;

			// 메시지 길이 제한
			if(200 < packet.Message.Length)
			{
				_lobbyLogger.LogWarning( "Player {SessionId} attempted to send too long message in lobby '{RoomName}' (Length: {Length})",
					  session.SessionId, RoomName, packet.Message.Length );
				return false;
			}

			// 스팸 방지 - 샘플
			if(packet.Message.Contains("spam") || packet.Message.Contains("SPAM"))
			{
				_lobbyLogger.LogWarning( "Player {SessionId} attempted to send spam message in lobby '{RoomName}': '{Message}'",
					  session.SessionId, RoomName, packet.Message );
				return false;
			}

			 return await base.ValidatePlayerChatAsync( session, packet);
		}

		protected override Task OnCleanupAsync()
		{
			_lobbyLogger.LogInformation( "LobbyRoom '{RoomName}' (ID: {RoomId}) cleanup started. Total visitors: {TotalVisitors}",
				 RoomName, RoomId, _totalVisitors );

			return base.OnCleanupAsync();
		}

		protected override MonsterSpawnPolicy GetMonsterSpawnPolicy()
		{
			return MonsterSpawnPolicy.LobbyDefault;
		}

		protected override void SetupDefaultSpawnPoints()
		{
			var mapData = RoomMap.MapData;
			float centerX = (mapData.Width * mapData.CellSize) / 2;
			float centerY = mapData.GroundY;
			float centerZ = (mapData.Depth * mapData.CellSize) / 2;

			// 로비는 평화로운 슬라임만 2마리
			MonsterManager.AddSpawnPoint( 2201, new Protocol.PosInfo
			{
				PosX = centerX - 10,
				PosY = centerY,
				PosZ = centerZ
			} );

			MonsterManager.AddSpawnPoint( 2201, new Protocol.PosInfo
			{
				PosX = centerX + 10,
				PosY = centerY,
				PosZ = centerZ
			} );
		}

		// 로비 환경 설정(초기화 시 호출)
		private async Task SetUpLobbyEnvironmentAsync()
		{
			// 로비 환경 설정 로직
			// ex ) 로비 NPC, 공지사항 등
			await Task.CompletedTask;
		}

		private async Task SendWelcomeMessageAsync(IClientSession session)
		{
			try
			{
				string welcomeMessage = IsDefaultLobby
					? $"환영합니다! {RoomName}에 입장하셨습니다."
					: $"환영합니다! 로비 {RoomName}에 입장하셨습니다.";

				Protocol.S_Chat welcomPacket = new Protocol.S_Chat
				{
					PlayerId = 0, // 시스템 메시지
					Message = welcomeMessage,
				};

				SendToPlayer( session, welcomPacket );
			}
			catch ( Exception ex )
			{
				_lobbyLogger.LogError( ex, "Failed to send welcome message to Player {SessionId} in lobby '{RoomName}'",
					  session.SessionId, RoomName );
			}
		}

		private async Task NotifyPlayerJoinedAsync(IClientSession session)
		{
			try
			{
				string joinMessage = $"Player_{session.SessionId}님이 로비에 입장했습니다.";
				Protocol.S_Chat joinPacket = new Protocol.S_Chat
				{
					PlayerId = session.Player.PlayerId,
					Message = joinMessage,
				};

				Broadcast( joinPacket, session ); // 본인 제외 브로드캐스트
			}
			catch (Exception ex)
			{
				_lobbyLogger.LogError( ex, "Failed to notify player join for Player {SessionId} in lobby '{RoomName}'",
					 session.SessionId, RoomName );
			}
		}

		private async Task NotifyPlayerLeftAsync(IClientSession session)
		{
			try
			{
				string leaveMessage = $"Player_{session.SessionId}님이 로비를 떠났습니다.";
				Protocol.S_Chat leavePacket = new Protocol.S_Chat
				{
					PlayerId = 0, // 시스템 메시지
					Message = leaveMessage,
				};

				Broadcast( leavePacket, session ); // 본인 제외 브로드 캐스트
			}
			catch(Exception ex)
			{
				_lobbyLogger.LogError( ex, "Failed to notify player leave for Player {SessionId} in lobby '{RoomName}'",
					  session.SessionId, RoomName );
			}
		}

		private async Task SendLobbyStatusAsync(IClientSession session)
		{
			try
			{
				string statusMessage = $"현재 로비 접속자: {CurrentPlayerCount}/{MaxPlayers}명";
				Protocol.S_Chat statusPacket = new Protocol.S_Chat
				{
					PlayerId = 0, // 시스템 메시지
					Message = statusMessage,
				};

				SendToPlayer( session, statusPacket );
			}
			catch(Exception ex)
			{
				_lobbyLogger.LogError( ex, "Failed to send lobby status to Player {SessionId} in lobby '{RoomName}'",
					  session.SessionId, RoomName );
			}
		}

		private async Task SetPlayerSpawnPositionAsync(IClientSession session)
		{
			try
			{
				// Position3DValidator를 사용해 로비 스폰 위치 계산
				var spawnPosition = Utils.Position3DValidator.GetSpawnPosition(this, new Random());

				session.Player.InitPosition( spawnPosition );
				RoomMap.AddPlayer( session, spawnPosition.PosX, spawnPosition.PosZ );

				// GameSession을 통해 Redis에 위치 업데이트
				await _playerPositionService.UpdatePositionAsync(session.PlayerId, spawnPosition );

				// 클라이언트에 위치 정보 전송
				var spawnPacket = new Protocol.S_Move
				{
					PlayerId = session.SessionId,
					PosInfo = spawnPosition
				};

				SendToPlayer( session, spawnPacket );

				_lobbyLogger.LogInformation( "Player {SessionId} 로비 스폰 위치 설정: ({X}, {Y}, {Z})",
					session.SessionId, spawnPosition.PosX, spawnPosition.PosY, spawnPosition.PosZ );
			}
			catch (Exception ex)
			{
				_lobbyLogger.LogError( ex, "Player {SessionId} 스폰 위치 설정 중 오류", session.SessionId );
			}
		}

		// 로비 통계 정보 조회
		public LobbyStatistics GetStatistics()
		{
			return new LobbyStatistics
			{
				RoomId = RoomId,
				RoomName = RoomName,
				CurrentPlayers = CurrentPlayerCount,
				MaxPlayers = MaxPlayers,
				TotalVisitors = TotalVisitors,
				CreatedAt = CreateAt,
				IsDefaultLobby = IsDefaultLobby,
				UptimeMinutes = (int)(DateTime.UtcNow - _createdAt).TotalMinutes
			};
		}
	}

	public class LobbyStatistics
	{
		public int RoomId { get; set; }
		public string RoomName { get; set; }
		public int CurrentPlayers { get; set; }
		public int MaxPlayers { get; set; }
		public int TotalVisitors { get; set; }
		public DateTime CreatedAt { get; set; }
		public bool IsDefaultLobby { get; set; }
		public int UptimeMinutes { get; set; }
	}
}
