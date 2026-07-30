using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Protocol;
using DatabaseLib.Entities;
using Server.Game;
using Server.Game.Objects;
using Server.Packet;
using Server.Room;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DatabaseLib.Redis;
using Server.Data;
using Server.Extensions;

namespace Server.Core.Session
{
    public class ClientSession : NetworkSession, IClientSession
    {
        private readonly PacketManager _packetManager;
        private readonly ISessionManager _sessionManager;
		private readonly SessionJobQueue _sessionQueue;
		private readonly IRedisService _redisService;
        private IRoom _currentRoom;

        private readonly object _roomLock = new object();
		private long _lastActiveTime = Environment.TickCount64;
		private int _state = (int)SessionState.Connected;
		private long _lastTokenRefreshTick = Environment.TickCount64;

		private const long TokenRefreshDebounceMs = 300000;

        public IRoom CurrentRoom
        {
            get { lock(_roomLock) { return _currentRoom; } }
        }

		public void SetCurrentRoom( IRoom room )
		{
			lock( _roomLock )
			{
				_currentRoom = room;
			}
		}
		public bool IsInRoom => _currentRoom != null;

		public long SessionId { get; private set; }
		public long AccountId { get; private set; }
		public string LoginToken { get; private set; }
		public long LastActiveTime => _lastActiveTime;
		public Player Player { get; private set; }
        public string PlayerName => Player?.Name ?? $"Session_{SessionId}";
        public long PlayerId => Player?.ObjectId ?? 0;		
		public SessionState State => (SessionState)Volatile.Read( ref _state );

		public bool IsAuthenticated => SessionState.Authenticated <= State && State < SessionState.Disconnecting;

		private static readonly Dictionary<SessionState, HashSet<SessionState>> _validTransitions = new()
		{
			[SessionState.Connected] = new() {SessionState.Authenticating, SessionState.Disconnecting },
			[SessionState.Authenticating] = new () {SessionState.Authenticated, SessionState.Connected, SessionState.Disconnecting},
			[SessionState.Authenticated] = new () {SessionState.EnteringGame, SessionState.Disconnecting },
			[SessionState.EnteringGame] = new() {SessionState.InRoom, SessionState.Authenticated, SessionState.Disconnecting },
			[SessionState.InRoom] = new() { SessionState.Transferring, SessionState.Disconnecting },
			[SessionState.Transferring] = new() {SessionState.InRoom, SessionState.Disconnecting },
			[SessionState.Disconnecting] = new() {SessionState.Disconnected },
			[SessionState.Disconnected] = new(),
		};

        public ClientSession( ILogger<ClientSession> logger, PacketManager packetManager, ISessionManager sessionManager, 
			IJobQueueManager jobQueueManager, IRedisService redisService, long sessionId)
			:base( logger )
		{
            _packetManager = packetManager;
            _sessionManager = sessionManager;
			_redisService = redisService;
            SessionId = sessionId;
			_sessionQueue = new SessionJobQueue( jobQueueManager, this, logger );
        }

		public void SetLoginToken( string token ) => LoginToken = token;
		public void BindAccountId(long accountId) => AccountId = accountId;

		public void EnqueueSystemJob( IJob job ) => _sessionQueue.Push( job );

		public void Send( IMessage packet )
		{
			ArraySegment<byte> segment = _packetManager.MakeSendPacket(packet);
			base.Send( segment );
		}
		public void Disconnect()
		{
			base.Close();
		}

		public override void OnRecvPacket( ArraySegment<byte> buffer )
        {
			if( _packetManager == null )
			{
				_logger.LogError( "PacketManager is null. Cannot handle received packet. SessionId: {SessionId}",
					SessionId );
				return;
			}

			Interlocked.Exchange( ref _lastActiveTime, Environment.TickCount64 );
			if(IsAuthenticated && string.IsNullOrEmpty(LoginToken) == false)
			{
				long now = Environment.TickCount64;
				long prev = Interlocked.Read(ref _lastTokenRefreshTick);
				if(TokenRefreshDebounceMs < now - prev && Interlocked.CompareExchange(ref _lastTokenRefreshTick, now, prev) == prev)
				{
					_ = _redisService.KeyExpireAsync( RedisKeys.TokenUserId( LoginToken ), TimeSpan.FromHours( 1 ) );
				}
			}
			PacketRoute packetRoute = _packetManager.RouteIncoming(this, buffer);

			_logger.LogDebug( "Packet Received. SessionId: {SessionId}, Size: {Size}", SessionId, buffer.Count );

			if( packetRoute.Dropped ) return;

			if( packetRoute.Category == PacketCategory.System )
			{
				// 순수 Push 경로는 CanAcceptJob을 검사하지 않으므로 종료 상태 가디를 여기서 둔다.
				if( State >= SessionState.Disconnecting ) return;
				EnqueueSystemJob( packetRoute.Job );					// 세션 큐 위임
			}
			else
			{
				BaseRoom baseRoom = CurrentRoom as BaseRoom;
				if( baseRoom == null )
				{
					_logger.LogError( "Room lost between routing ans push. SessionId = {SessionId}, Category={Category}, State={State}",
						SessionId, packetRoute.Category, State );
					return;
				}
				baseRoom.Push( packetRoute.Job );						// 룸 큐 위임
			}
        }

        public override void OnSend( int bytes )
        {
            _logger.LogDebug( "Packet Sent. SessionId: {SessionId}, Size: {Size}", SessionId, bytes );
        }

		public override void OnConnected( EndPoint endPoint )
		{
            _logger.LogInformation( "Client Connected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", SessionId, endPoint );

            // 세션 매니저에 세션 등록
            _sessionManager.RegisterSession( this );
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			_logger.LogInformation( "Client Disconnected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", SessionId, endPoint );

			// 상태 전이 - 필터 즉시 활성화
			if(TryTransitionTo(SessionState.Disconnecting) == false)
			{
				_logger.LogWarning( "Already disconnecting. SessionId={SessionId}", SessionId );
				return;
			}

			_sessionManager.NotifyDisconnecting( this, DisconnectReason.ClientDisconnect );

			IRoom room = CurrentRoom;
			BaseRoom baseRoom = room as BaseRoom;
			_ = HandleDisconnectAsync( endPoint, baseRoom );
		}

		public bool TryTransitionTo( SessionState next )
		{
			SessionState current = (SessionState)Volatile.Read(ref _state);

			if(_validTransitions.TryGetValue(current, out var allowed) == false || allowed.Contains(next) == false)
			{
				_logger.LogWarning( "Invalid state transition: {Current} -> {Next} (SessionId={SessionId})", current, next, SessionId );
				return false;
			}

			int prev = Interlocked.CompareExchange(ref _state, (int)next, (int)current);
			if(prev != (int)current)
			{
				// 다른 스레드가 먼저 전이시킴
				return false;
			}

			_logger.LogDebug( "Session {SessionId} state: {From} -> {To}", SessionId, current, next );
			return true;
		}

		// 플레이어 생성
		public void CreatePlayer( IDataManager dataManager, long playerId, string playerName)
		{
			if(Player != null)
			{
				_logger.LogWarning( "이미 Player가 존재합니다: SessionId={SessionId}", SessionId );
				return;
			}

			Player = new Player( dataManager, playerId, playerName );
			SubscribePlayerEvents();
		}

		private void SubscribePlayerEvents()
		{
			if(Player != null)
			{
				Player.OnHealthChanged += OnPlayerHealthChanged;
				Player.OnManaChanged += OnPlayerManaChanged;
				Player.OnLevelUp += OnPlayerLevelUp;

				//Player.OnItemAdded += OnPlayerItemAdded;
				//Player.OnItemRemoved += OnPlayerItemRemoved;
				//Player.OnItemQuantityChanged += OnPlayerItemQuantityChanged;
				Player.OnInventoryUpdated += OnPlayerInventoryUpdated;

				//Player.OnItemEquipped += OnPlayerItemEquipped;
				//Player.OnItemUnequipped += OnPlayerItemUnequipped;
				//Player.OnEquipmentStatsChanged += OnEquipmentStatsChanged;
				Player.OnEquipmentChanged += OnPlayerEquipmentChanged;

				Player.OnDeath += OnPlayerDeath;
				Player.OnStateChanged += OnPlayerStateChanged;
			}
		}

		private void UnsubscribePlayerEvents()
		{
			if(Player != null)
			{
				Player.OnHealthChanged -= OnPlayerHealthChanged;
				Player.OnManaChanged -= OnPlayerManaChanged;
				Player.OnLevelUp -= OnPlayerLevelUp;

				//Player.OnItemAdded -= OnPlayerItemAdded;
				//Player.OnItemRemoved -= OnPlayerItemRemoved;
				//Player.OnItemQuantityChanged -= OnPlayerItemQuantityChanged;
				Player.OnInventoryUpdated -= OnPlayerInventoryUpdated;
				//Player.OnItemUnequipped -= OnPlayerItemUnequipped;
				//Player.OnItemEquipped -= OnPlayerItemEquipped;
				//Player.OnEquipmentStatsChanged -= OnEquipmentStatsChanged;
				Player.OnEquipmentChanged -= OnPlayerEquipmentChanged;

				Player.OnDeath -= OnPlayerDeath;
				Player.OnStateChanged -= OnPlayerStateChanged;
			}
		}

		private async Task HandleDisconnectAsync(EndPoint endPoint, BaseRoom baseRoom)
		{
			try
			{
				_logger.LogInformation( "Client Disconnected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}",
					SessionId, endPoint );

				// Room에서 퇴장 (Queue 경유, await로 완료 보장)
				if(baseRoom != null)
				{
					bool left = await baseRoom.LeaveViaQueueAsync(this);
					if(left == false)
						_logger.LogWarning( "Disconnect cleanup leave returned false. SessionId: {SessionId}", SessionId );
				}
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to leave room during disconnect. SessionId: {SessionId}", SessionId );
			}
			finally
			{
				// Leave 완료 후 정리 (순서 보장)
				UnsubscribePlayerEvents();
				_sessionManager.UnregisterSession( SessionId );
				// 상태 전이 - 완료
				TryTransitionTo( SessionState.Disconnected );
			}
		}

        // HP 변경 이벤트 핸들러
        private void OnPlayerHealthChanged(IGameObject obj, int oldHP, int newHP)
        {
			if(State >= SessionState.Disconnecting)
				return;

            S_PlayerUpdate packet = new S_PlayerUpdate
            {
                Player = obj.ToObjectInfo()
            };

            Send(packet);

			_logger.LogDebug( "[Event] Player HP Changed: PlayerId={PlayerId}, {OldHP} → {NewHP}",
                obj.ObjectId, oldHP, newHP);
		}

        // MP 변경 이벤트 핸들러
        private void OnPlayerManaChanged( IGameObject obj, int oldMP, int newMP)
        {
			if(State >= SessionState.Disconnecting)
				return;

			S_PlayerUpdate packet = new S_PlayerUpdate
			{
				Player = obj.ToObjectInfo(),
			};

			Send( packet );

			_logger.LogDebug( "[Event] Player MP Changed: PlayerId={PlayerId}, {OldMP} → {NewMP}",
				obj.ObjectId, oldMP, newMP );
		}

        // 레벨 업 이벤트 핸들러
        private void OnPlayerLevelUp(Player player)
        {
			if(State >= SessionState.Disconnecting)
				return;

			S_LevelUp packet = new S_LevelUp
            {
                PlayerId = player.ObjectId,
                NewLevel = player.Level,
                NewMaxHP = player.MaxHP,
                NewMaxMP = player.MaxMP,

            };

            // 현재 룸의 모든 플레이어에게 브로드캐스트
            if(CurrentRoom != null)
            {
                // async 메서드를 동기적으로 호출(이벤트 핸들러는 void 반환)
                CurrentRoom.Broadcast( packet );
            }

			_logger.LogInformation( "[Event] Player Level Up: PlayerId={PlayerId},NewLevel={NewLevel}, HP={MaxHP}, MP={MaxMP}",
                player.ObjectId, player.Level, player.MaxHP, player.MaxMP);
		}

		private void OnPlayerInventoryUpdated(Player player, InventoryUpdateEventArgs eventArgs)
		{
			if(State >= SessionState.Disconnecting)
				return;

			var packet = new S_InventoryUpdate();
			if(eventArgs.NewGold.HasValue)
				packet.NewGold = eventArgs.NewGold.Value;
			packet.ChangedItems.AddRange( eventArgs.ChangedItems.Select( e => e.ToProto() ).ToList() );
			packet.RemovedItemInstanceIds.AddRange( eventArgs.RemovedItemInstanceIds );

			Send( packet );

			_logger.LogInformation( "[Event] Inventory Changed: Player={PlayerId}", PlayerId );
		}

		private void OnPlayerEquipmentChanged(Player player)
		{
			if(State >= SessionState.Disconnecting)
				return;

			var packet = new S_EquipmentUpdate();
			packet.Success = true;
			packet.Reason = string.Empty;
			packet.Slots.AddRange( player.ToEquipmentRefs() );
			packet.UpdatedStats = player.GetStatInfo();

			Send( packet );

			_logger.LogInformation( "[Event] Item EquipChanged: PlayerId={PlayerId}", PlayerId );
		}

        // 플레이어 죽음 이벤트 핸들러
        private void OnPlayerDeath(IGameObject obj)
        {
			if(State >= SessionState.Disconnecting)
				return;

			_ = HandlePlayerDeath( obj );
		}

		private async Task HandlePlayerDeath(  IGameObject obj )
		{
			if(State >= SessionState.Disconnecting)
				return;

			try
			{
				BaseRoom baseRoom = CurrentRoom as BaseRoom;
				if(baseRoom == null)
				{
					_logger.LogWarning( "Player is not in a valid room during death handling. PlayerId={PlayerId}", obj.ObjectId );
					return;
				}

				// 룸 내 다른 플레이어에게 사망 패킷 브로드캐스트
				S_Despawn packet = new S_Despawn();
				packet.Objects.Add( obj.ToObjectInfo() );
				CurrentRoom.BroadcastInRange( packet, obj.PosInfo, this );

				// Room에서 퇴장 (Queue 경유, await로 완료 보장)
				bool left = await baseRoom.LeaveViaQueueAsync(this);
				if(left == false)
					_logger.LogError( "Player failed to leave room after death. PlayerId={PlayerId}, RoomId={RoomId}",
						obj.ObjectId, baseRoom.RoomId );

				// 사망 후 리스폰 처리 (3초 딜레이) - Room에서 스케줄링하여 처리, 리스폰 시 룸 재입장 처리 포함
				baseRoom.ScheduleRespawn( this, 3000 );

				_logger.LogInformation( "Player OnDeath Handler. SessionId: {SessionId}, PlayerId: {PlayerId}", SessionId, obj.ObjectId);
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to OnPlayerDeath Event Handler. SessionId: {SessionId}, PlayerId: {PlayerId}", SessionId, obj.ObjectId );
			}
		}

		private void OnPlayerStateChanged( IGameObject obj, int oldState, int newState )
		{
			if(State >= SessionState.Disconnecting)
				return;

			S_PlayerUpdate packet = new S_PlayerUpdate
			{
				Player = obj.ToObjectInfo(),
			};

			Send( packet );

			_logger.LogDebug( "[Event] Player State Changed: PlayerId={PlayerId}, {OldState} → {NewState}",
				obj.ObjectId, oldState, newState );
		}

		//public bool TakeDamage( int damage)
		//{
		//    if(Player == null) return false;

			//    bool result = Player.TakeDamage(damage, 0);
			//    if(result)
			//    {
			//        if(Player.CreatureState == State.Dead)
			//        {
			//            // TODO : 플레이어 Dead 상태 전달 필요.
			//        }
			//    }

			//    return result;
			//}

			//public bool Heal(int amount)
			//{
			//    if(Player == null) return false;

			//    return Player.Heal(amount);
			//}

			//public bool GainExperience(long exp)
			//{
			//    if(Player == null) return false;

			//    return Player.GainExperience(exp);
			//}

			// 현재 상태 정보 조회
		public GameSessionInfo GetSessionInfo()
        {
            return new GameSessionInfo
            {
                SessionId = SessionId,
                PlayerName = PlayerName,
                IsInRoom = IsInRoom,
                CurrentRoomId = CurrentRoom?.RoomId,
                CurrentRoomName = CurrentRoom?.RoomName
            };
        }

        // 전체 플레이어 정보 반환 메서드
        public ObjectInfo GetPlayerFullInfo()
        {
			return Player.ToObjectInfo();
        }

		// 디버깅용
		public override string ToString()
		{
			return $"GameSession(Id: {SessionId}, Room: {CurrentRoom?.RoomId})";
		}

		internal void ForceState(SessionState state) => Volatile.Write(ref _state, (int)state);
	}

    public class GameSessionInfo
    {
        public long SessionId {  get; set; }
        public string PlayerName { get; set; }
        public bool IsInRoom { get; set; }
        public int? CurrentRoomId { get; set; }
        public string CurrentRoomName { get; set; }
    }
}
