using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Protocol;
using Server.Database.Entities;
using Server.Game;
using Server.Game.Objects;
using Server.Packet;
using Server.Room;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace Server.Core.Session
{
    public class ClientSession : ServerCore.NetworkSession, IClientSession
    {
        private readonly ILogger<ClientSession> _logger;
        private readonly PacketManager _packetManager;
        private readonly ISessionManager _sessionManager;
        private IRoom _currentRoom;
        private readonly object _roomLock = new object();
		private long _lastActiveTime = Environment.TickCount64;

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
		public long LastActiveTime => _lastActiveTime;
		public Player Player { get; private set; }
        public string PlayerName => Player.Name ?? $"Player_{Player.ObjectId}";
        public long PlayerId => Player.ObjectId;

        public ClientSession( ILogger<ClientSession> logger, PacketManager packetManager, ISessionManager sessionManager, long sessionId)
        {
            _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
            _packetManager = packetManager;
            _sessionManager = sessionManager;
            SessionId = sessionId;
        }

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
            ushort packetIdValue = BitConverter.ToUInt16(buffer.Array, buffer.Offset + 2);
            PacketID packetId = (PacketID)packetIdValue;

            _logger.LogDebug( "Packet Received. SessionId: {SessionId}, PacketID: {PacketID}, Size: {Size}",
                SessionId, packetId, buffer.Count );

            if(_packetManager != null)
            {
				Interlocked.Exchange( ref _lastActiveTime, Environment.TickCount64 );
				_ = _packetManager.HandlePacket( this, buffer );
			}
			else
			{
				_logger.LogError( "PacketManager is null. Cannot handle received packet. SessionId: {SessionId}, PacketID: {PacketID}",
					SessionId, packetId );
			}
        }

        public override void OnSend( int bytes )
        {
            //LogManager.Debug("Packet Sent. SessionId: {SessionId}, Size: {Size}", this.SessionId, bytes);
            _logger.LogDebug( "Packet Sent. SessionId: {SessionId}, Size: {Size}", SessionId, bytes );
        }

		public override void OnConnected( EndPoint endPoint )
		{
            _logger.LogInformation( "Client Connected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", SessionId, endPoint );

            // 플레이어 정보 초기화.
            InitializePlayer();

            // 세션 매니저에 세션 등록
            _sessionManager.RegisterSession( this );
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			_logger.LogInformation( "Client Disconnected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", SessionId, endPoint );

            // Player 이벤트 구독 해제 추가
            if(Player != null)
            {
                Player.OnHealthChanged -= OnPlayerHealthChanged;
                Player.OnManaChanged -= OnPlayerManaChanged;
                Player.OnLevelUp -= OnPlayerLevelUp;
                Player.OnItemAdded -= OnPlayerItemAdded;
                Player.OnItemRemoved -= OnPlayerItemRemoved;
                Player.OnItemUnequipped -= OnPlayerItemUnequipped;
                Player.OnItemEquipped -= OnPlayerItemEquipped;
                Player.OnEquipmentStatsChanged -= OnEquipmentStatsChanged;
                Player.OnDeath -= OnPlayerDeath;
            }

           // 플레이어 상태를 Disconnected상태로 처리
			//Player?.Disconnect();

            // 세션 매니저에서 세션 해제
            _sessionManager.UnregisterSession( SessionId );
		}

        // 플레이어 초기화
        private void InitializePlayer()
        {
			Player = new Player( SessionId, null );

            Player.OnHealthChanged += OnPlayerHealthChanged;
            Player.OnManaChanged += OnPlayerManaChanged;
            Player.OnLevelUp += OnPlayerLevelUp;

            Player.OnItemAdded += OnPlayerItemAdded;
            Player.OnItemEquipped += OnPlayerItemEquipped;
            Player.OnItemUnequipped += OnPlayerItemUnequipped;
            Player.OnItemRemoved += OnPlayerItemRemoved;
            Player.OnEquipmentStatsChanged += OnEquipmentStatsChanged;
            Player.OnDeath += OnPlayerDeath;
			Player.OnStateChanged += OnPlayerStateChanged;

			_logger.LogInformation( "Player initialized: {PlayerInfo}", Player.ToString() );
        }

        // HP 변경 이벤트 핸들러
        private void OnPlayerHealthChanged(IGameObject obj, int oldHP, int newHP)
        {
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

        // 아이템 추가 이벤트 핸들러
        private void OnPlayerItemAdded(Player player, int slot, InventoryItem item)
        {
            // InventoryItem -> InventoryItemInfo 변환
            S_ItemAdded packet = new S_ItemAdded
            {
                Item = new InventoryItemInfo
                {
                    ItemId = item.ItemId,
                    Quantity = item.Quantity,
                    Slot = item.Slot,
                    EnhancementLevel = item.Enhancement?.Level ?? 0,
                    CustomName = item.CustomName ?? "",
                    AcquiredAt = item.AcquiredAt.HasValue
                    ? ((DateTimeOffset)item.AcquiredAt.Value).ToUnixTimeSeconds() : 0
                },
                Source = "Monster Drop"
            };

            // Options 딕셔너리 복사
            if(item.Options != null && item.Options.Any())
            {
                foreach(var kvp in item.Options)
                {
                    packet.Item.Options.Add(kvp.Key, kvp.Value);
                }
            }

            Send( packet );

			_logger.LogInformation( "[Event] Item Added: PlayerId={PlayerId}, ItemId={ItemId}, Slot={Slot}, Qty={Quantity}",
                player.ObjectId, item.ItemId, item.Slot, item.Quantity);
		}

        // 장비 착용 이벤트 핸들러
        private void OnPlayerItemEquipped(Player player, PlayerEquipment.EquipSlot slot, InventoryItem item)
        {
            // 현재 장비 상태 조회
            var equipmentData = player.GetEquipmentData();

            S_ItemEquipped packet = new S_ItemEquipped
            {
                Success = true,
                InventorySlot = item?.Slot ?? -1,
                EquipSlot = (int)slot,
                UpdatedEquipment = new EquipmentInfo()
            };

            // dictionary를 순회하면서 EquipmentInfo 채우기
            foreach(var kvp in equipmentData)
            {
                switch(kvp.Key)
                {
                case PlayerEquipment.EquipSlot.Weapon:
                    packet.UpdatedEquipment.WeaponItemId = kvp.Value.ItemId;
                    break;
                case PlayerEquipment.EquipSlot.Armor:
                    packet.UpdatedEquipment.ArmorItemId = kvp.Value.ItemId;
                    break;
				case PlayerEquipment.EquipSlot.Helmet:
					packet.UpdatedEquipment.HelmetItemId = kvp.Value.ItemId;
					break;
				case PlayerEquipment.EquipSlot.Gloves:
					packet.UpdatedEquipment.GlovesItemId = kvp.Value.ItemId;
					break;
				}
            }

			packet.UpdatedStats = player.GetStatInfo();

            Send( packet );

			_logger.LogInformation( "[Event] Item Equipped: PlayerId={PlayerId}, Slot={Slot}, ItemId={ItemId}",
                player.ObjectId, slot, item?.ItemId ?? 0);
		}

        private void OnPlayerItemUnequipped(Player player, PlayerEquipment.EquipSlot slot, InventoryItem item)
        {
            var equipmentData = player.GetEquipmentData();

            S_ItemUnequipped packet = new S_ItemUnequipped
            {
                Success = true,
                EquipSlot = (int)slot,
                ReturnedToSlot = item?.Slot ?? -1,
                UpdatedEquipment = new EquipmentInfo()
            };

			// EquipmentInfo 채우기 (장착 핸들러와 동일)
			foreach(var kvp in equipmentData)
			{
				switch(kvp.Key)
				{
				case PlayerEquipment.EquipSlot.Weapon:
					packet.UpdatedEquipment.WeaponItemId = kvp.Value.ItemId;
					break;
				case PlayerEquipment.EquipSlot.Armor:
					packet.UpdatedEquipment.ArmorItemId = kvp.Value.ItemId;
					break;
				case PlayerEquipment.EquipSlot.Helmet:
					packet.UpdatedEquipment.HelmetItemId = kvp.Value.ItemId;
					break;
				case PlayerEquipment.EquipSlot.Gloves:
					packet.UpdatedEquipment.GlovesItemId = kvp.Value.ItemId;
					break;
				}
			}

            packet.UpdatedStats = player.GetStatInfo();

            Send( packet );

			_logger.LogInformation( "[Event] Item Unequipped: PlayerId={PlayerId}, Slot={Slot}, ReturnedSlot={ReturnedSlot}",
                player.ObjectId, slot, item?.Slot ?? -1);
		}

        // 아이템 제거 이벤트 핸들러
        private void OnPlayerItemRemoved(Player player, int slot, InventoryItem item)
        {
            // 제거된 아이템 정보 변환
            InventoryItemInfo changedItem = new InventoryItemInfo
            {
                ItemId = item.ItemId,
                Quantity = 0,           // 제거되었으므로 0
                Slot = slot,
            };

            S_InventoryUpdate packet = new S_InventoryUpdate
            {
                ChangedItems = {changedItem },
                NewGold = player.Inventory.Gold
            };

            Send( packet );

			_logger.LogInformation( "[Event] Item Removed: PlayerId={PlayerId}, ItemId={ItemId}, Slot={Slot}",
                player.ObjectId, item.ItemId, slot);
		}

        // 장비 스탯 변경 이벤트 핸들러
		// TODO - 장비 변경 이벤트인데, 플레이어 정보를 보냄, 이는 장비 변경으로 인해 플레이어 스탯 정보를 변경해야한다는 의미 - 현재 구조와 다름.
        private void OnEquipmentStatsChanged(Player player, Dictionary<PlayerEquipment.StatType, int> stats)
        {
            S_PlayerStat packet = new S_PlayerStat
            {
                Player = player.ToObjectInfo(),
            };

			Send( packet );

			_logger.LogDebug( "[Event] Equipment Stats Changed: PlayerId={PlayerId}",
                player.ObjectId );
		}

        // 플레이어 죽음 이벤트 핸들러
        private void OnPlayerDeath(IGameObject obj)
        {
			S_Despawn packet = new S_Despawn();
			packet.Objects.Add( obj.ToObjectInfo() );
			IRoom reEnterRoom = CurrentRoom;
			if(CurrentRoom != null)
            {
				// 룸 내 다른 플레이어에게 사망 패킷 브로드캐스트
				CurrentRoom.BroadcastInRange ( packet, obj.PosInfo, this );
				bool result = CurrentRoom.TryLeaveAsync( this ).GetAwaiter().GetResult();
				if( result == false)
				{
					_logger.LogError( "Player failed to leave room after death. PlayerId={PlayerId}, RoomId={RoomId}",
						obj.ObjectId, CurrentRoom.RoomId );
				}
			}

			// 사망 후 리스폰 처리 (3초 딜레이)
			if(reEnterRoom != null)
			{
				const int respawnDelayMs = 3000;
				reEnterRoom.ScheduleJob( () =>
				{
					try
					{
						Player.Revive();

						var result = reEnterRoom.TryEnterAsync(this).GetAwaiter().GetResult();
						if(RoomEnterResult.Success != result)
						{
							_logger.LogWarning( "Respawn Failed to Re-Enter Room. PlayerId={PlayerId}", PlayerId );
							return;
						}

						reEnterRoom.SendToPlayer( this, new S_EnterGame
						{
							Player = Player.ToObjectInfo(),
							MapId = reEnterRoom.RoomMap.MapId,
						} );
					}
					catch(Exception ex)
					{
						_logger.LogError( ex, "Respawn Error for PlayerId: {PlayerId}", PlayerId );
					}
				}, respawnDelayMs );
			}

			_logger.LogWarning( "[Event] Player Death: PlayerId={PlayerId}", obj.ObjectId );
		}

		private void OnPlayerStateChanged( IGameObject obj, int oldState, int newState )
		{
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
