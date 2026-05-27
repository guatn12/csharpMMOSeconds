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
using Server.Data.Models;
using Server.Services.Combat;
using Server.Services.Reward;
using Server.Game.Monsters;
using Server.Services;
using Server.Packet.Handlers;
using Server.Game.Map;
using Server.Utils;
using Server.Game.Objects;

namespace Server.Room
{
	public abstract class BaseRoom : JobSerializer, IRoom, IDisposable
	{
		protected readonly ILogger _logger;
		protected readonly ILoggerFactory _loggerFactory;
		protected readonly ConcurrentDictionary<long, IClientSession> _players;
		public ObjectManager ObjectManager { get; private set; }
		public GameMap RoomMap { get; protected set; }
		protected readonly object _lock = new object();
		private bool _dispose = false;

		public int RoomId { get; private set; }
		public string RoomName { get; protected set; }
		public int MaxPlayers { get; protected set; }

		public int CurrentPlayerCount => _players.Count;
		public abstract RoomType RoomType { get; }
		public RoomState State { get; protected set; } = RoomState.Created;

		// 몬스터
		private readonly Func<IRoom, DataManager, ObjectManager, ILogger, MonsterSpawnPolicy, IMonsterManager> _monsterManagerFactory;
		public IMonsterManager MonsterManager { get; private set; }
		protected readonly DataManager _dataManager;

		public IReadOnlyList<IClientSession> Players => _players.Values.ToList();
		public bool IsEmpty => _players.IsEmpty;
		public bool IsFull => MaxPlayers <= _players.Count;

		#region RoomTransitionCoordinator
		private int _pendingEnterCount;				// Target 보호: 들어오는 중인 세션 수
		private int _activeTransitionSourceCount;   // Source 보호: 이 방을 source로 진행 중인 Transition 수 (rollback 대비)
		private int _respawnPendingCount;			// 부활 대기 카운터 - 사망 후 ScheduleRespawn으로 등록된 잡이 fire될 때까지 _players가 비더라도 룸 파괴를 차단.
		
		/// <summary> Target: 예약 슬롯 확보. State/Full 검증 + 카운터 증가가 _lock 아래 원자 단위 </summary>
		internal bool TryReserveEnter()
		{
			lock(_lock)
			{
				if(State != RoomState.Active && State != RoomState.Created) return false;
				if(MaxPlayers <= _players.Count + _pendingEnterCount) return false;
				_pendingEnterCount++;
				return true;
			}
		}

		/// <summary> 예약 슬롯 반납(rollback / 흐름 중단 시 사용하는 단독 경로 </summary>
		internal void ReleaseEnterReservation()
		{
			lock(_lock)
			{
				if(_pendingEnterCount > 0) _pendingEnterCount--;
			}
		}

		/// <summary>
		/// 예약을 든 채 입장. 예약 소진은 입장 성공/실패/예외 어느 경로로 끝나든 finally에서 1회 수행된다. - release ownership이 baseRoom 안에서 완결되므로
		/// Coordinator가 중복 해제할 여지가 없다. release는 TryEnterAsync가 완전히 끝난 뒤(release-last) 실행 -> 성공 경로에선 이미 세션이 _players에 있어
		/// 멤버십으로 보호되고, 그 이전 구간은 _pendingEnterCount >= 1로 보호된다.
		/// </summary>
		internal async ValueTask<RoomEnterResult> EnterWithReservationAsync(IClientSession session)
		{
			try
			{
				return await TryEnterAsync( session, consumesReservation: true );
			}
			finally
			{
				ReleaseEnterReservation();		// 경로 무관 예약 1회 소진
			}
		}

		/// <summary> Source 보호 시작 - rollback 대비 </summary>
		internal void BeginSourceProtection()
		{
			lock(_lock)
			{
				_activeTransitionSourceCount++;
			}
		}

		internal void EndSourceProtection()
		{
			lock(_lock)
			{
				if(_activeTransitionSourceCount > 0) _activeTransitionSourceCount--;
			}
		}

		/// <summary>
		/// 파괴 안전성 최종 확성 - 비어있고 진행 중 transaction이 없으면 State를 Closing으로 전환하고 true 판정
		/// </summary>
		internal bool TryBeginClose()
		{
			lock (_lock)
			{
				if(State != RoomState.Active && State != RoomState.Created)
					return false;
				if(_players.Count != 0 || _pendingEnterCount != 0 || _activeTransitionSourceCount != 0 || _respawnPendingCount != 0)
					return false;
				State = RoomState.Closing;
				return true;
			}
		}

		/// <summary> 부활 잡 등록 직후 호출 - cleanup 차단 신호 </summary>
		internal void BeginRespawnPending()
		{
			lock(_lock) { _respawnPendingCount++; }
		}

		internal void EndRespawnPending()
		{
			lock(_lock)
			{
				if(_respawnPendingCount > 0) _respawnPendingCount--;
			}
		}

		
		#endregion
		// Service
		protected readonly ICombatService _combatService;
		protected readonly IRewardService _rewardService;
		protected readonly IPlayerPositionService _playerPositionService;

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

		protected BaseRoom( ILogger logger, ILoggerFactory loggerFactory, int roomId, string roomName, int maxPlayers, DataManager dataManager,
			IJobQueueManager jobQueueManager, ICombatService combatService, IRewardService rewardService,
			IPlayerPositionService playerPositionService,
			Func<IRoom, DataManager, ObjectManager, ILogger, MonsterSpawnPolicy, IMonsterManager> monsterManagerFactory = null,
			int mapId = 1 )
			: base( jobQueueManager )
		{
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			_loggerFactory = loggerFactory;
			_dataManager = dataManager;

			_combatService = combatService;
			_rewardService = rewardService;
			_playerPositionService = playerPositionService;

			RoomId = roomId;
			RoomName = roomName ?? throw new ArgumentNullException( nameof( roomName ) );
			MaxPlayers = 0 < maxPlayers ? maxPlayers : throw new ArgumentOutOfRangeException( nameof( maxPlayers ) );

			// 3D 룸 크기 설정
			MapData mapData = _dataManager.GetMap(mapId);
			if(mapData == null)
			{
				_logger.LogWarning( "MapData not found for MapId {MapId}, using default room size", mapId );
				mapData = MapData.CreateEmpty( 20, 20, 5.0f );
			}

			RoomMap = new GameMap( mapData );

			_players = new ConcurrentDictionary<long, IClientSession>();
			ObjectManager = new ObjectManager( _loggerFactory.CreateLogger<ObjectManager>() );

			InitializePacketHandlers( _loggerFactory, combatService, rewardService, playerPositionService );

			_monsterManagerFactory = monsterManagerFactory ?? ( ( room, dataMgr, objMgr, logger, policy ) =>
				new MonsterManager( room, dataMgr, objMgr, logger, policy ) );

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

			// 이벤트 구독 해제
			if(MonsterManager != null)
			{
				MonsterManager.OnMonsterDespawned -= OnMonsterDespawned;
				MonsterManager.OnMonsterSpawned -= OnMonsterSpawned;
			}

			MonsterManager.Dispose();
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

		protected virtual async Task<RoomEnterResult> TryEnterAsync( IClientSession session, bool consumesReservation = false )
		{
			if(session == null)
				return RoomEnterResult.InvalidState;

			// Validate
			bool reservationMissing = false;
			lock(_lock)
			{
				// 상태 검증
				if(State != RoomState.Active && State != RoomState.Created)
					return RoomEnterResult.RoomClosed;

				// 이미 룸에 있는지 확인
				if(_players.ContainsKey( session.SessionId ))
					return RoomEnterResult.AlreadyInRoom;

				// 룸이 가득 찬 상태인지 확인
				int reservationDiscount = (consumesReservation && _pendingEnterCount > 0) ? 1 : 0;
				if(consumesReservation && _pendingEnterCount == 0)
					reservationMissing = true;
				if(MaxPlayers <= _players.Count + _pendingEnterCount - reservationDiscount)
					return RoomEnterResult.RoomFull;
			}

			if(reservationMissing)
				_logger.LogWarning( "예약 입장인데 _pendingEnterCount == 0 - 예약 불변식 위반 의심 (sessionId={SessionId})", session.SessionId );

			// Prepare
			// 플레이어 위치 초기화
			await OnInitPlayerPosition( session );

			// Apply
			bool applied = false;
			lock (_lock)
			{
				// 플레이어 추가
				if(!_players.TryAdd( session.SessionId, session ))
				{
					applied = false;
				}
				else
				{
					// 룸이 가득 찼는지 상태 업데이트
					if(MaxPlayers <= _players.Count) State = RoomState.Full;
					applied = true;
				}
			}

			if(!applied)
			{
				// Prepare 부작용 rollback
				RoomMap.Remove( session.Player );
				await _playerPositionService.RemovePositionAsync( session.PlayerId );
				return RoomEnterResult.UnknownError;
			}

			// 오브젝트 매니저에 플레이어 등록.
			ObjectManager.Register( session.Player );
			// 입장 시 session의 currentRoom 변경
			session.SetCurrentRoom( this );
			// 이벤트 발생
			PlayerEntered?.Invoke( this, new PlayerRoomEventArgs( session, this ) );

			_logger.LogInformation( "Player {SessionId} entered room {RoomId} ({CurrentCount}/{MaxPlayers})",
					session.SessionId, RoomId, CurrentPlayerCount, MaxPlayers );

			// Notify
			// 룸 별 입장 로직 실행 - 주로 입장 시 초기 정보 전송 담당
			await OnPlayerEnterAsync( session );
			return RoomEnterResult.Success;
		}

		protected virtual async Task<bool> TryLeaveAsync( IClientSession session )
		{
			if(session == null || !_players.ContainsKey( session.SessionId ))
				return false;

			return await InternalLeaveAsync( session, false );
		}

		protected virtual bool TryLeave( IClientSession session )
		{
			if(session == null || !_players.ContainsKey( session.SessionId ))
				return false;

			return InternalLeave( session, false );
		}

		/// <summary>
		/// 외부에서 Room의 JobQueue를 경유하여 입장 처리
		/// Room 내부 핸들러에서는 호출 금지 (데드락 위험)
		/// </summary>
		public Task<RoomEnterResult> EnterViaQueueAsync(IClientSession session)
		{
			return PushAsync<RoomEnterResult>( () => new ValueTask<RoomEnterResult>( TryEnterAsync( session ) ) );
		}

		/// <summary>
		/// 외부에서 Room의 JobQueue를 경유하여 퇴장 처리
		/// </summary>
		public Task<bool> LeaveViaQueueAsync(IClientSession session)
		{
			return PushAsync<bool>( () => new ValueTask<bool>( TryLeaveAsync( session ) ) );
		}

		protected override bool CanAcceptJob()
		{
			return State != RoomState.Closing;
		}

		public virtual void Broadcast( IMessage packet, IClientSession excludeSession = null )
		{
			if(packet == null)
				return;

			List<IClientSession> currentPlayers = _players.Values.ToList();

			foreach(var player in currentPlayers)
			{
				if(player != excludeSession)
				{
					SendToPlayer( player, packet );
				}
			}
		}

		public virtual void BroadcastInRange(IMessage packet, PosInfo position, IClientSession excludeSession = null )
		{
			if(packet == null)
				return;

			var currentPlayers = RoomMap.GetNearByPlayers(position.PosX, position.PosZ,
				_dataManager.GameConfig.ViewDistance);

			foreach(var player in currentPlayers)
			{
				IClientSession session = FindPlayerToPlayerId(player.ObjectId);
				if(session == null)
				{
					_logger.LogWarning("Session Not Found From ObjectId {ObjectId}", player.ObjectId);
					continue;
				}

				if(excludeSession != null && session == excludeSession)
					continue;

				SendToPlayer( session, packet );
			}
		}

		public virtual void SendToPlayer( IClientSession session, IMessage packet )
		{
			if(session == null || packet == null)
				return;

			session.Send( packet );
		}

		public void ScheduleJob(Action action, int delayMs)
		{
			DelegateJob job = _jobQueueManager.JobPool.Get<DelegateJob>();
			job.Initialize( action );
			ScheduleTimer( job, delayMs );
		}

		public void ScheduleJob(Func<Task> actionAsync, int delayMs)
		{
			DelegateJob job = _jobQueueManager.JobPool.Get<DelegateJob>();
			job.Initialize( actionAsync );
			ScheduleTimer( job, delayMs );
		}

		// 룸의 주기적 하트비트 - 빈작업 사용
		public void Tick()
		{
			DelegateJob job = _jobQueueManager.JobPool.Get<DelegateJob>();
			job.Initialize( () => { } );
			Push( job );
		}

		public void ScheduleRespawn(IClientSession session, int delayMs)
		{
			BeginRespawnPending();  // 외부 컨텍스트에서 동기 +1

			bool scheduled = false;
			try
			{
				ScheduleJob( async () =>
				{
					try
					{
						if(session == null || session.Player == null)
							return;

						// TODO: 활성 세션 여부도 여기서 확인 필요

						session.Player.Revive();

						RoomEnterResult result = await TryEnterAsync(session);
						if(result != RoomEnterResult.Success)
						{
							_logger.LogWarning( "Failed to respawn player {SessionId} in room {RoomId} - Enter Result: {Result}",
								session.SessionId, RoomId, result );
							return;
						}

						SendToPlayer( session, new S_EnterGame
						{
							Player = session.Player.ToObjectInfo(),
							MapId = RoomMap.MapId
						} );
					}
					catch(Exception ex)
					{
						_logger.LogError( ex, "Failed to respawn player {SessionId} in room {RoomId}", session.SessionId, RoomId );
					}
					finally
					{
						EndRespawnPending();    // 경로(성공/실패/예외) 무관 1회 -1
					}
				}, delayMs );
				scheduled = true;
			}
			finally
			{
				if(!scheduled)
					EndRespawnPending();		// ScheduleJob 자체가 throw한 경우 즉시 -1(누수 방지)
			}
			
		}

		protected virtual Task OnInitializeAsync() => Task.CompletedTask;
		protected virtual Task OnCleanupAsync() => Task.CompletedTask;
		protected virtual async Task OnPlayerEnterAsync( IClientSession session )
		{
			// 현재 스폰된 몬스터 중 플레이어 근처 몬스터 정보 전송
			var monsters = RoomMap.GetNearByMonster(session.Player.PosInfo.PosX, session.Player.PosInfo.PosZ,
					_dataManager.GameConfig.ViewDistance );

			var spawnPacket = new S_Spawn();
			foreach(var monster in monsters)
			{
				spawnPacket.Objects.Add( monster.ToObjectInfo() );
			}

			// 근처 플레이어 정보 전송
			var players = RoomMap.GetNearByPlayers( session.Player.PosInfo.PosX, session.Player.PosInfo.PosZ,
				_dataManager.GameConfig.ViewDistance );

			foreach(var player in players)
			{
				if(player.ObjectId == session.PlayerId)
					continue;

				spawnPacket.Objects.Add( player.ToObjectInfo() );
			}

			SendToPlayer( session, spawnPacket );

			// 근처 플레이어 들에게 들어온 플레이어 전송
			var playerSpawnPacket = new S_Spawn();
			playerSpawnPacket.Objects.Add( session.Player.ToObjectInfo() );
			BroadcastInRange( playerSpawnPacket, session.Player.PosInfo, excludeSession: session );
		}
		protected abstract Task OnInitPlayerPosition( IClientSession session );
		protected virtual async Task OnPlayerLeaveAsync( IClientSession session )
		{
			// 룸 퇴장 패킷 전달
			var leavePacket = new S_LeaveGame();
			leavePacket.ObjectId = session.PlayerId;
			SendToPlayer( session, leavePacket );
		}
		protected virtual void OnPlayerLeave(IClientSession session)
		{
			// 룸 퇴장 패킷 전달
			var leavePacket = new S_LeaveGame();
			leavePacket.ObjectId = session.PlayerId;
			SendToPlayer( session, leavePacket );
		}

		// 몬스터 초기화 메서드 추가
		protected virtual async Task InitializeMonsterManagerAsync()
		{
			// Room 타입별로 다른 Policy 적용
			var policy = GetMonsterSpawnPolicy();

			// MonsterManager 생성
			MonsterManager = _monsterManagerFactory( this, _dataManager, ObjectManager, _loggerFactory.CreateLogger<MonsterManager>(), policy );

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

			// 주기적 업데이트 타이머 예약
			UpdateMonsters();

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
		private void OnMonsterDespawned( long monsterId, Monster monster )
		{
			try
			{
				// gameMap에서 몬스터 위치 정보 제거
				if(monster != null)
				{
					RoomMap.Remove( monster );
				}

				// S_MonsterDespawn 브로드캐스트
				S_Despawn despawnPacket = new S_Despawn();
				despawnPacket.Objects.Add( monster.ToObjectInfo() );

				// async 메서드를 동기적으로 실행 (JobQueue 안에서)
				BroadcastInRange( despawnPacket, monster.PosInfo );

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
				// gameMap에 몬스터 위치 정보 추가
				if(monster != null)
				{
					RoomMap.Add( monster, monster.PosInfo.PosX, monster.PosInfo.PosZ );
				}

				// S_MonsterSpawn 브로드 캐스트
				S_Spawn spawnPacket = new S_Spawn();
				spawnPacket.Objects.Add( monster.ToObjectInfo() );

				// async 메서드를 동기적으로 실행 (JobQueue 안에서)
				BroadcastInRange( spawnPacket, monster.PosInfo );

				_logger.LogInformation( "Broadcasted S_MonsterSpawn for Monster {MonsterId} ({Name}) in Room {RoomId}",
					monster.ObjectId, monster.Name, RoomId );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to broadcast spawn for Monster {MonsterId} in Room {RoomId}",
					monster.ObjectId, RoomId );
			}
		}

		// 기본 스폰 포인트 설정 (하위 클래스에서 재정의)
		protected virtual void SetupDefaultSpawnPoints()
		{
			var monsterDataList = _dataManager.GetAllMonsters();

			foreach( var monster in monsterDataList )
			{
				var position = Position3DValidator.GetSpawnPosition(this);
				MonsterManager.AddSpawnPoint( monster.Key, position );
			}
		}

		// 몬스터 업데이트 메서드
		protected virtual void UpdateMonsters()
		{
			if(MonsterManager == null) return;
			// MonsterUpdateJob 생성 및 초기화
			DelegateJob job = _jobQueueManager.JobPool.Get<DelegateJob>();
			job.Initialize( () =>
			{
				if(MonsterManager == null) return;

				try
				{
					MonsterManager.Update();
				}
				catch(Exception ex)
				{
					_logger.LogError( ex, "Error updating monsters in Room {RoomId}", RoomId );
				}
				finally
				{
					UpdateMonsters();
				}
			} );

			ScheduleTimer( job, 100 );
		}

		private async Task<bool> ForceLeaveAsync( IClientSession session )
		{
			return await InternalLeaveAsync( session, true );
		}

		private async Task<bool> InternalLeaveAsync( IClientSession session, bool isForced )
		{
			InternalLeave( session, isForced );
			return true;
		}

		private bool InternalLeave(IClientSession session, bool isForced )
		{
			// 오브젝트 매니저에서 플레이어 제거
			ObjectManager.Unregister( session.PlayerId );

			if(!_players.TryRemove( session.SessionId, out var removedSession ))
				return false;

			// 퇴장 시 room의 맵에서 플레이어 위치 정보 제거
			RoomMap.Remove( session.Player );

			// 퇴장 시 session의 currentRoom 초기화
			session.SetCurrentRoom( null );

			try
			{
				// 룸 별 퇴장 로직 실행
				OnPlayerLeave( session );

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

		private void InitializePacketHandlers( ILoggerFactory loggerFactory, ICombatService combatService, IRewardService rewardService,
			IPlayerPositionService playerPositionService )
		{
			RoomPacketHandler = new RoomPacketHandler( loggerFactory.CreateLogger<RoomPacketHandler>(), this, playerPositionService );
			CombatPacketHandler = new CombatPacketHandler( loggerFactory.CreateLogger<CombatPacketHandler>(), this, combatService, rewardService, _dataManager );
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

		protected virtual void Dispose( bool disposing )
		{
			if(!_dispose && disposing)
			{
				CleanupAsync().GetAwaiter().GetResult();
				_dispose = true;
			}
		}


	}
}
