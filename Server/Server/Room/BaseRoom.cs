using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Database.Entities;
using ServerCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Server.Game;
using Server.Data;
using Server.Core.Jobs;
using Server.Services.Combat;
using Server.Services.Reward;
using Server.Game.Monsters;
using Server.Services;
using Server.Packet.Handlers;

namespace Server.Room
{
	public abstract class BaseRoom : JobSerializer, IRoom, IDisposable
	{
		protected readonly ILogger _logger;
		protected readonly ILoggerFactory _loggerFactory;
		protected readonly ConcurrentDictionary<long, IClientSession> _players;
		protected readonly object _lock = new object();
		private static int _nextRoomId = 1;
		private bool _dispose = false;

		public int RoomId { get; private set; }
		public string RoomName { get; protected set; }
		public int MaxPlayers { get; protected set; }

		// 룸 크기 속성 
		public float RoomWidth { get; protected set; } = 100.0f;    // x 축 크기
		public float RoomHeight { get; protected set; } = 50.0f;    // y 축 크기
		public float RoomDepth { get; protected set; } = 100.0f;    // z 축 크기

		// 룸 경계 정보를 위한 속성
		public float MinX { get; protected set; } = 0.0f;
		public float MaxX => MinX + RoomWidth;
		public float MinY { get; protected set; } = 0.0f;
		public float MaxY => MinY + RoomHeight;
		public float MinZ { get; protected set; } = 0.0f;
		public float MaxZ => MinZ + RoomDepth;

		public int CurrentPlayerCount => _players.Count;
		public abstract RoomType RoomType { get; }
		public RoomState State { get; protected set; } = RoomState.Created;

		public IReadOnlyList<IClientSession> Players => _players.Values.ToList();
		public bool IsEmpty => _players.IsEmpty;
		public bool IsFull => MaxPlayers <= _players.Count;

		// 몬스터
		public IMonsterManager MonsterManager { get; private set; }
		protected readonly DataManager _dataManager;
		private System.Threading.Timer _monsterUpdateTimer;

		private bool _isMonsterUpdateScheduled = false; // Timer 누적 방지

		// Service
		protected readonly ICombatService _combatService;
		protected readonly IRewardService _rewardService;
		protected readonly PlayerPositionService _playerPositionService;

		// Category 핸들러
		protected SystemPacketHandler SystemPacketHandler { get; private set; }
		protected RoomPacketHandler RoomPacketHandler { get; private set; }
		protected CombatPacketHandler CombatPacketHandler { get; private set; }
		protected InventoryPacketHandler InventoryPacketHandler { get; private set; }

		RoomPacketHandler IRoom.RoomPacketHandler => RoomPacketHandler;
		CombatPacketHandler IRoom.CombatPacketHandler => CombatPacketHandler;
		InventoryPacketHandler IRoom.InventoryPacketHandler => InventoryPacketHandler;

		public event EventHandler<PlayerRoomEventArgs> PlayerEntered;
		public event EventHandler<PlayerRoomEventArgs> PlayerLeft;

		protected BaseRoom( ILogger logger, ILoggerFactory loggerFactory, string roomName, int maxPlayers, DataManager dataManager,
			IJobQueueManager jobQueueManager, ICombatService combatService, IRewardService rewardService,
			PlayerPositionService playerPositionService,
			float roomWidth = 100.0f, float roomHeight = 50.0f, float roomDepth = 100.0f,
			float minX = 0.0f, float minY = 0.0f, float minZ = 0.0f )
			: base( jobQueueManager )
		{
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			_loggerFactory = loggerFactory;
			_dataManager = dataManager;

			_combatService = combatService;
			_rewardService = rewardService;
			_playerPositionService = playerPositionService;

			RoomId = GenerateNextRoomId();
			RoomName = roomName ?? throw new ArgumentNullException( nameof( roomName ) );
			MaxPlayers = 0 < maxPlayers ? maxPlayers : throw new ArgumentOutOfRangeException( nameof( maxPlayers ) );

			// 3D 룸 크기 설정
			RoomWidth = roomWidth;
			RoomHeight = roomHeight;
			RoomDepth = roomDepth;

			MinX = minX;
			MinY = minY;
			MinZ = minZ;

			_players = new ConcurrentDictionary<long, IClientSession>();

			InitializePacketHandlers( _loggerFactory, combatService, rewardService, playerPositionService );
			
			_logger.LogInformation( "Room created: {RoomId} '{RoomName}' (Type: {RoomType}, Max: {MaxPlayers})",
				RoomId, RoomName, RoomType, MaxPlayers );
		}

		public virtual async Task InitializeAsync()
		{
			lock(_lock)
			{
				if(State != RoomState.Created)
				{
					_logger.LogWarning( "Room {RoomId} already initialized (State: {State})", RoomId, State );
					return;
				}

				State = RoomState.Active;
			}

			// 몬스터 스폰 시스템 초기화
			//InitializeMonsterSpawner();
			await InitializeMonsterManagerAsync();

			await OnInitializeAsync();

			_logger.LogInformation( "Room {RoomId} '{RoomName}' initialized successfully", RoomId, RoomName );
		}

		public virtual async Task CleanupAsync()
		{
			lock(_lock)
			{
				if(State == RoomState.Closed)
				{
					return;
				}

				State = RoomState.Closing;
			}

			// 몬스터 업데이트 타이머 정지
			_monsterUpdateTimer?.Dispose();
			_monsterUpdateTimer = null;

			// 이벤트 구독 해제
			if(MonsterManager != null)
			{
				MonsterManager.OnMonsterDespawned -= OnMonsterDespawned;
				MonsterManager.OnMonsterSpawned -= OnMonsterSpawned;
			}

			MonsterManager?.Dispose();
			MonsterManager = null;

			// 모든 플레이어 강제 퇴장
			List<IClientSession> playersToRemove = _players.Values.ToList();
			foreach(var player in playersToRemove)
			{
				await ForceLeaveAsync( player );
			}

			await OnCleanupAsync();
		}

		public bool ContainsPlayer( IClientSession session )
		{
			return session != null && _players.ContainsKey( session.SessionId );
		}

		public bool ContainsPlayerToPlayerId( long playerId )
		{
			return 0 < playerId && _players.Values.Where( p => p.PlayerId == playerId ).Any();
		}

		public IClientSession FindPlayer( int sessionId )
		{
			_players.TryGetValue( sessionId, out var session );
			return session;
		}

		public IClientSession FindPlayerToPlayerId( long playerId )
		{
			return _players.Values.Where( p => p.PlayerId == playerId ).FirstOrDefault();
		}

		public virtual async Task<RoomEnterResult> TryEnterAsync( IClientSession session )
		{
			if(session == null)
				return RoomEnterResult.InvalidState;

			lock(_lock)
			{
				// 상태 검증
				if(State != RoomState.Active && State != RoomState.Created)
					return RoomEnterResult.RoomClosed;

				// 이미 룸에 있는지 확인
				if(_players.ContainsKey( session.SessionId ))
					return RoomEnterResult.AlreadyInRoom;

				// 룸이 가득 찬 상태인지 확인
				if(MaxPlayers <= _players.Count)
					return RoomEnterResult.RoomFull;

				// 플레이어 추가
				if(!_players.TryAdd( session.SessionId, session ))
					return RoomEnterResult.UnknownError;

				// 룸이 가득 찼는지 상태 업데이트
				if(MaxPlayers <= _players.Count)
					State = RoomState.Full;
			}

			try
			{
				// 입장 시 session의 currentRoom 변경
				session.CurrentRoom = this;

				// 룸 별 입장 로직 실행
				await OnPlayerEnterAsync( session );

				// 이벤트 발생
				PlayerEntered?.Invoke( this, new PlayerRoomEventArgs( session, this ) );

				_logger.LogInformation( "Player {SessionId} entered room {RoomId} ({CurrentCount}/{MaxPlayers})",
					session.SessionId, RoomId, CurrentPlayerCount, MaxPlayers );

				return RoomEnterResult.Success;
			}
			catch(Exception e)
			{
				// 실패 시 플레이어 제거
				_players.TryRemove( session.SessionId, out _ );
				if(session.CurrentRoom != null )
				{
					session.CurrentRoom = null;		// 실패 시 session의 현재 룸도 초기화.
				}
				
				_logger.LogError( e, "Failed to enter player {SessionId} to room {RoomId}", session.SessionId, RoomId );
				return RoomEnterResult.UnknownError;
			}
		}

		public virtual async Task<bool> TryLeaveAsync( IClientSession session )
		{
			if(session == null || !_players.ContainsKey( session.SessionId ))
				return false;

			return await InternalLeaveAsync( session, false );
		}

		public virtual async Task BroadcastAsync( IMessage packet, IClientSession excludeSession = null )
		{
			if(packet == null)
				return;

			List<IClientSession> currentPlayers = _players.Values.ToList();
			List<Task> tasks = new List<Task>();

			foreach(var player in currentPlayers)
			{
				// TODO : Send 호출 플레이어 제외 임시 주석 처리. 주석 다시 해제
				if(player != excludeSession)
				{
					tasks.Add( SendToPlayerAsync( player, packet ) );
				}
			}

			if(0 < tasks.Count)
			{
				await Task.WhenAll( tasks );
			}
		}

		public virtual async Task SendToPlayerAsync( IClientSession session, IMessage packet )
		{
			if(session == null || packet == null)
				return;

			try
			{
				await Task.Run( () => session.Send( packet ) );
			}
			catch(Exception e)
			{
				_logger.LogError( e, "Failed to send packet to player {SessionId} in room {RoomId}",
					session.SessionId, RoomId );
			}
		}

		protected virtual Task OnInitializeAsync() => Task.CompletedTask;
		protected virtual Task OnCleanupAsync() => Task.CompletedTask;
		protected virtual async Task OnPlayerEnterAsync( IClientSession session )
		{
			// 현재 스폰된 몬스터 정보 전송
			if(MonsterManager != null)
			{
				var aliveMonsters = MonsterManager.GetAliveMonsters();
				if(0 < aliveMonsters.Count)
				{
					var monsterSpawnPacket = new Protocol.S_MonsterSpawn();
					foreach(var monster in aliveMonsters)
					{
						monsterSpawnPacket.Monsters.Add( monster.Info );
					}
					await SendToPlayerAsync( session, monsterSpawnPacket );

					_logger.LogDebug( "Sent {Count} monsters info to Player {SessionId}",
						aliveMonsters.Count, session.SessionId );
				}
			}
		}
		protected virtual Task OnPlayerLeaveAsync( IClientSession session ) => Task.CompletedTask;
		public virtual Task OnPlayerMoveAsync( IClientSession session, Protocol.C_Move packet ) => Task.CompletedTask;
		public virtual Task OnPlayerChatAsync( IClientSession session, Protocol.C_Chat packet ) => Task.CompletedTask;
		public virtual Task OnPlayerUseSkillAsync( IClientSession session, Protocol.C_UseSkill packet ) => Task.CompletedTask;

		public virtual Task<bool> ValidatePlayerMoveAsync( IClientSession session, Protocol.C_Move packet )
		{
			// 기본 3D 위치 검증
			bool isValid = Utils.Position3DValidator.IsValidPosition(packet.PosInfo, this);

			if(!isValid)
			{
				_logger.LogWarning( "Invalid move attempt by player {SessionId} in room{ RoomId}: Position ({X}, {Y}, {Z}) is outside room bounds ({MinX}-{MaxX},{ MinY}-{ MaxY}, { MinZ}-{ MaxZ})",
					session.SessionId, RoomId, packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ,
					MinX, MaxX, MinY, MaxY, MinZ, MaxZ );
			}

			return Task.FromResult( isValid );
		}
		public virtual Task<bool> ValidatePlayerChatAsync( IClientSession session, Protocol.C_Chat packet ) => Task.FromResult( true );
		public virtual Task<bool> ValidatePlayerInfoAsync( IClientSession session, Protocol.C_PlayerInfo packet ) => Task.FromResult( true );
		public virtual Task<bool> ValidatePlayerUseSkillAsync( IClientSession session, Protocol.C_UseSkill packet ) => Task.FromResult( true );

		// 몬스터 초기화 메서드 추가
		protected virtual async Task InitializeMonsterManagerAsync()
		{
			// Room 타입별로 다른 Policy 적용
			var policy = GetMonsterSpawnPolicy();

			// MonsterManager 생성
			MonsterManager = new MonsterManager(room: this, _dataManager, _logger, policy);

			// MonsterManager 초기화
			await MonsterManager.InitializeAsync();

			// MonsterSpawner 이벤트 구독 (MonsterManager를 통해)
			MonsterManager.OnMonsterDespawned += OnMonsterDespawned;
			MonsterManager.OnMonsterSpawned += OnMonsterSpawned;

			// 스폰 포인트 설정 (BaseRoom이 담당)
			SetupDefaultSpawnPoints();

			// 초기 몬스터 스폰
			MonsterManager.SpawnInitialMonsters();

			// MonsterSpawner 이벤트 구독 (BaseRoom에서 패킷 브로드캐스트)
			// Note: MonsterSpawner는 MonsterManager 내부에 있으므로 직접 접근 불가
			// 대신 MonsterManager가 이벤트를 중계해야 함 (추후 개선 필요)

			// 주기적 업데이트 타이머 시작 (100ms마다)
			_monsterUpdateTimer = new System.Threading.Timer(
				callback: _ => UpdateMonsters(),
				state: null,
				dueTime: TimeSpan.FromMilliseconds( 100 ),
				period: TimeSpan.FromMilliseconds( 100 ) );

			_logger.LogInformation( "Monster spawner initialized for room {RoomId}", RoomId );
		}

		protected virtual MonsterSpawnPolicy GetMonsterSpawnPolicy()
		{
			return MonsterSpawnPolicy.Default;
		}

		/// <summary>
		/// MonsterSpawner에서 딜레이 Despawn이 완료되었을 때 호출됨
		/// JobQueue Worker 스레드에서 실행되므로 스레드 안전
		/// </summary>
		private void OnMonsterDespawned( long monsterId )
		{
			try
			{
				// S_MonsterDespawn 브로드캐스트
				S_MonsterDespawn despawnPacket = new S_MonsterDespawn();
				despawnPacket.MonsterIds.Add( monsterId );

				// async 메서드를 동기적으로 실행 (JobQueue 안에서)
				BroadcastAsync( despawnPacket ).GetAwaiter().GetResult();

				_logger.LogInformation( "Broadcasted S_MonsterDespawn for Monster {MonsterId} in Room {RoomId}",
					monsterId, RoomId );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to broadcast despawn for Monster {MonsterId} in Room {RoomId}",
					monsterId, RoomId );
			}
		}

		/// <summary>
		/// MonsterSpawner에서 몬스터 리스폰이 완료되었을 때 호출됨
		/// JobQueue Worker 스레드에서 실행되므로 스레드 안전
		/// </summary>
		private void OnMonsterSpawned( Monster monster )
		{
			try
			{
				// S_MonsterSpawn 브로드 캐스트
				S_MonsterSpawn spawnPacket = new S_MonsterSpawn();
				spawnPacket.Monsters.Add( monster.Info );

				// async 메서드를 동기적으로 실행 (JobQueue 안에서)
				BroadcastAsync( spawnPacket ).GetAwaiter().GetResult();

				_logger.LogInformation( "Broadcasted S_MonsterSpawn for Monster {MonsterId} ({Name}) in Room {RoomId}",
					monster.MonsterId, monster.Name, RoomId );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to broadcast spawn for Monster {MonsterId} in Room {RoomId}",
					monster.MonsterId, RoomId );
			}
		}

		// 기본 스폰 포인트 설정 (하위 클래스에서 재정의)
		protected virtual void SetupDefaultSpawnPoints()
		{
			//ex) 룸 중앙에 슬라임 3마리 스폰
			float centerX = MinX + RoomWidth / 2;
			float centerY = MinY;
			float centerZ = MinZ + RoomDepth / 2;

			MonsterManager.AddSpawnPoint( 2201, new PosInfo
			{
				PosX = centerX - 5,
				PosY = centerY,
				PosZ = centerZ
			} );

			MonsterManager.AddSpawnPoint( 2201, new PosInfo
			{
				PosX = centerX + 5,
				PosY = centerY,
				PosZ = centerZ
			} );

			MonsterManager.AddSpawnPoint( 2001, new PosInfo
			{
				PosX = centerX,
				PosY = centerY,
				PosZ = centerZ + 10
			} );
		}

		// 몬스터 업데이트 메서드
		protected virtual void UpdateMonsters()
		{
			// Timer 누적 호출 방지
			if(_isMonsterUpdateScheduled)
			{
				_logger.LogDebug( "Monster update already scheduled for room {RoomId}, skipping", RoomId );
				return;
			}

			if(MonsterManager == null)
			{
				_logger.LogWarning( "MonsterManager is null for room {RoomId}", RoomId );
				return;
			}

			_isMonsterUpdateScheduled = true;

			// MonsterUpdateJob 생성 및 초기화
			MonsterUpdateJob job = _jobQueueManager.JobPool.Get<MonsterUpdateJob>();
			job.Initialize( MonsterManager, RoomId, _logger );

			// JobQueue에 비동기 추가
			try
			{
				Push( job );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to push MonsterUpdateJob to queue for room {RoomId}", RoomId );
			}
			finally
			{
				// 큐잉 시도 완료 후 플래그 해제(즉시 완료됨)
				_isMonsterUpdateScheduled = false;
			}
		}



		private static int GenerateNextRoomId()
		{
			return System.Threading.Interlocked.Increment( ref _nextRoomId );
		}

		private async Task<bool> ForceLeaveAsync( IClientSession session )
		{
			return await InternalLeaveAsync( session, true );
		}

		private async Task<bool> InternalLeaveAsync( IClientSession session, bool isForced )
		{
			if(!_players.TryRemove( session.SessionId, out var removedSession ))
				return false;

			try
			{
				// 룸 별 퇴장 로직 실행
				await OnPlayerLeaveAsync( session );

				// 상태 업데이트
				lock(_lock)
				{
					if(State == RoomState.Full && _players.Count < MaxPlayers)
						State = RoomState.Active;
				}

				// 이벤트 발생
				PlayerLeft?.Invoke( this, new PlayerRoomEventArgs( session, this ) );

				var leaveType = isForced ? "forced to leave" : "left";
				_logger.LogInformation( "Player {SessionId} {LeaveType} room {RoomId} ({CurrentCount}/{MaxPlayers})",
					session.SessionId, leaveType, RoomId, CurrentPlayerCount, MaxPlayers );

				return true;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to remove Player {SessionId} from room {RoomId}", session.SessionId, RoomId );
				return false;
			}
		}

		private int GetLastInventorySlot( Player player )
		{
			InventoryModel inventoryData = player.GetInventoryData();
			return inventoryData.Items.LastOrDefault()?.Slot ?? -1;
		}

		private bool ValidateSession( IClientSession session, ILogger logger )
		{
			if(session?.Player == null)
			{
				logger.LogWarning( "Invalid session or player in Room {RoomId}", RoomId );
				return false;
			}
			return true;
		}

		private bool ValidateUseItemPacket( C_UseItem packet, ILogger logger )
		{
			if(packet == null || packet.Slot < 0 || packet.Slot >= 50 || packet.Quantity <= 0 || packet.Quantity > 10)
			{
				logger.LogWarning( "Invalid use item packet: Slot={Slot},Quantity={Quantity}",
					packet?.Slot ?? -1, packet?.Quantity ?? -1 );
				return false;
			}
			return true;
		}

		private void InitializePacketHandlers( ILoggerFactory loggerFactory, ICombatService combatService, IRewardService rewardService,
			PlayerPositionService playerPositionService )
		{
			RoomPacketHandler = new RoomPacketHandler( loggerFactory.CreateLogger<RoomPacketHandler>(), this, playerPositionService );
			CombatPacketHandler = new CombatPacketHandler( loggerFactory.CreateLogger<CombatPacketHandler>(), this, combatService, rewardService );
			InventoryPacketHandler = new InventoryPacketHandler( loggerFactory.CreateLogger<InventoryPacketHandler>(), this );

		}

#if DEBUG

		protected override void OnProcessJobsStart()
		{
			 _logger.LogDebug("ProcessJobs Start - Room:{RoomId}, Thread:{ThreadId}",
			 RoomId, System.Threading.Thread.CurrentThread.ManagedThreadId);
		}

		protected override void OnProcessJobsEnd()
		{
			_logger.LogDebug("ProcessJobs End - Room:{RoomId}, Thread:{ThreadId}",
			RoomId, System.Threading.Thread.CurrentThread.ManagedThreadId);
		}

#endif


		public void Dispose()
		{
			Dispose( true );
			GC.SuppressFinalize( this );
		}

		public Monster GetMonster( long monsterId )
		{
			return MonsterManager?.GetMonster( monsterId );
		}

		protected virtual void Dispose( bool disposing )
		{
			if(!_dispose && disposing)
			{
				_monsterUpdateTimer?.Dispose();
				CleanupAsync().GetAwaiter().GetResult();
				_dispose = true;
			}
		}


	}
}
