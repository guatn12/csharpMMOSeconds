using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Protocol;
using Server.Database.Entities;
using Server.Game;
using Server.Infra;
using Server.Packet;
using Server.Room;
using Server.Services;
using ServerCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Server.Core.Session
{
    public class GameSession : ServerCore.Session
    {
        private readonly RedisService _redisService;
        private readonly ILogger<GameSession> _logger;
        private readonly IRoomManager _roomManager;
        private readonly PacketManager _packetManager;
        private readonly PlayerPositionService _playerPositionService;
        private IRoom _currentRoom;
        private readonly object _roomLock = new object();

        public IRoom CurrentRoom
        {
            get
            {
                lock(_roomLock)
                {
                    return _currentRoom;
                }
            }
            internal set
            {
                lock(_roomLock)
                {
                    _currentRoom = value;
                }
            }
        }
		public bool IsInRoom => _currentRoom != null;

		public long SessionId { get; private set; }
        public Player Player { get; private set; }
        public string PlayerName => Player.PlayerName ?? $"Player_{Player.PlayerId}";
        public long PlayerId => Player.PlayerId;

        private static long _nextSessionId = 1;

        public GameSession( ILogger<GameSession> logger, IRoomManager roomManager, 
            RedisService redisService, PacketManager packetManager,
            PlayerPositionService playerPositionService)
        {
            _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
            _roomManager = roomManager ?? throw new ArgumentNullException( nameof( _roomManager ) );
            _redisService = redisService;
            _packetManager = packetManager;
            _playerPositionService = playerPositionService;
        }

        private static long GenerateNextSessionId()
        {
            return System.Threading.Interlocked.Increment( ref _nextSessionId );
        }

		public void Send( IMessage packet )
		{
			ArraySegment<byte> segment = _packetManager.MakeSendPacket(packet);
			base.Send( segment );
		}

		public override void OnRecvPacket( ArraySegment<byte> buffer )
        {
            ushort packetIdValue = BitConverter.ToUInt16(buffer.Array, buffer.Offset + 2);
            PacketID packetId = (PacketID)packetIdValue;

            _logger.LogDebug( "Packet Received. SessionId: {SessionId}, PacketID: {PacketID}, Size: {Size}",
                SessionId, packetId, buffer.Count );

            if(_packetManager != null)
            {
                Task.Run( async () =>
                {
                    await _packetManager.HandlePacket( this, buffer );
                } );
            }

            //Program.PacketManagerInstance.HandlePacket( this, buffer );
        }

        public override void OnSend( int bytes )
        {
            //LogManager.Debug("Packet Sent. SessionId: {SessionId}, Size: {Size}", this.SessionId, bytes);
            _logger.LogDebug( "Packet Sent. SessionId: {SessionId}, Size: {Size}", SessionId, bytes );
        }

		// 비동기 패킷 전송
		public async Task SendAsync( IMessage packet )
		{
			await Task.Run( () => Send( packet ) );
		}

		public override void OnConnected( EndPoint endPoint )
		{
            SessionId = GenerateNextSessionId();
            //LogManager.Info("Client Connected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint);
			_logger.LogInformation( "Client Connected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", SessionId, endPoint );

            // 플레이어 정보 초기화.
            InitializePlayer();

            // redis에 세션 정보 저장.
            _ = Task.Run( async () =>
            {
                var sessionInfo = new
                {
                    SessionId = SessionId,
                    ConnectedAt = DateTime.UtcNow,
                    EndPoint = endPoint.ToString(),
                    PlayerId = Player.Info.PlayerId
                };

                await _redisService.SetSessionAsync( SessionId, sessionInfo, TimeSpan.FromHours( 2 ) );
                _logger.LogDebug( "Redis에 세션 정보 저장 완료: SessionId={SessionId}", SessionId );
            } );

			// 기본 로비에 자동 입장 시도.
			_ = Task.Run( async () => await TryJoinDefaultLobbyAsync() );
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			//LogManager.Info("Client Disconnected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint);
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

            // Redis에서 세션 정보 삭제
            _ = Task.Run( async () =>
            {
                await _redisService.DeleteSessionAsync( SessionId );
                _logger.LogDebug( "Redis에서 세션 정보 삭제 완료: SessionId={SessionId}", SessionId );
            } );

            // Redis 에서 플레이어 위치 정보 삭제.
            _ = Task.Run( async () =>
            {
                await _playerPositionService.RemovePositionAsync( Player.PlayerId );
                _logger.LogDebug( "플레이어 위치 정보 삭제 완료: PlayerId={PlayerId}",
                    Player.PlayerId );
            } );

			// 플레이어 상태를 Disconnected상태로 처리
			Player?.Disconnect();

			// 모든 룸에서 퇴장 처리
			_ = Task.Run( async () => await LeaveAllRoomsAsync() );
		}

		// 기본 로비 입장 시도
		public async Task<bool> TryJoinDefaultLobbyAsync()
        {
            try
            {
                _logger.LogDebug( "Attempting to join default lobby for Player {SessionId}", SessionId );

                RoomEnterResult result = await _roomManager.JoinDefaultLobbyAsync(this);
                if(result == RoomEnterResult.Success)
                {
                    S_EnterGame enterPacket = new S_EnterGame
                    {
                        Player = Player.Info
                    };
                    
                    await SendAsync( enterPacket );

                    _logger.LogInformation( "Player {SessionId} successfully joined default lobby (Room: {RoomId})",
                        SessionId, CurrentRoom?.RoomId );
                    return true;
                }
                else
                {
                    _logger.LogWarning( "Player {SessionId} failed to join default lobby: {Result}",
                        SessionId, result );
                    return false;
                }
            }
            catch(Exception ex)
            {
                _logger.LogError( ex, "Error joining default lobby for Player {SessionId}", SessionId );
                return false;
            }
        }

        // 특정 룸으로 이동
        public async Task<RoomEnterResult> MoveToRoomAsync( int roomId )
        {
            try
            {
                _logger.LogDebug( "Player {SessionId} attempting to move to Room {RoomId}", SessionId, roomId );

                IRoom previousRoom = CurrentRoom;
                RoomEnterResult result = await _roomManager.MovePlayerToRoomAsync(this, roomId);

                if(result == RoomEnterResult.Success)
                {
                    _logger.LogInformation( "Player {SessionId} moved from Room {PreviousRoomId} to Room {NewRoomId}",
                          SessionId, previousRoom?.RoomId, CurrentRoom?.RoomId );
                }

                return result;
            }
            catch(Exception ex)
            {
                _logger.LogError( ex, "Error moving Player {SessionId} to Room {RoomId}", SessionId, roomId );
                return RoomEnterResult.UnknownError;
            }
        }

        // 현재 룸에서 퇴장
        public async Task<bool> LeaveCurrentRoomAsync()
        {
            IRoom room = CurrentRoom;
            if(room == null)
            {
                _logger.LogDebug( "Player {SessionId} is not in any room", SessionId );
                return false;
            }

            try
            {
                _logger.LogDebug( "Player {SessionId} leaving Room {RoomId}", SessionId, room.RoomId );

                bool success = await room.TryLeaveAsync(this);
                if(success)
                {
                    CurrentRoom = null;
                    _logger.LogInformation( "Player {SessionId} left Room {RoomId}", SessionId, room.RoomId );
                }

                return success;
            }
            catch(Exception ex)
            {
                _logger.LogError( ex, "Error leaving room for Player {SessionId}", SessionId );
                return false;
            }
        }

        // 모든 룸에서 퇴장 (연결 해제 시 호출)
        public async Task LeaveAllRoomsAsync()
        {
            try
            {
                bool success = await _roomManager.RemovePlayerFromAllRoomsAsync(this);
                if(success)
                {
                    CurrentRoom = null;
                    _logger.LogInformation( "Player {SessionId} removed from all rooms", SessionId );
                }
            }
            catch(Exception ex)
            {
                _logger.LogError( ex, "Error removing Player {SessionId} from all rooms", SessionId );
            }
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

			_logger.LogInformation( "Player initialized: {PlayerInfo}", Player.ToString() );
        }

        // HP 변경 이벤트 핸들러
        private void OnPlayerHealthChanged(Player player, int oldHP, int newHP)
        {
            S_PlayerUpdate packet = new S_PlayerUpdate
            {
                Player = player.Info
            };

            Send(packet);

			_logger.LogDebug( "[Event] Player HP Changed: PlayerId={PlayerId}, {OldHP} → {NewHP}",
                player.PlayerId, oldHP, newHP);
		}

        // MP 변경 이벤트 핸들러
        private void OnPlayerManaChanged(Player player, int oldMP, int newMP)
        {
			S_PlayerUpdate packet = new S_PlayerUpdate
			{
				Player = player.Info
			};

			Send( packet );

			_logger.LogDebug( "[Event] Player MP Changed: PlayerId={PlayerId}, {OldMP} → {NewMP}",
				player.PlayerId, oldMP, newMP );
		}

        // 레벨 업 이벤트 핸들러
        private void OnPlayerLevelUp(Player player)
        {
            S_LevelUp packet = new S_LevelUp
            {
                PlayerId = player.PlayerId,
                NewLevel = player.Level,
                NewMaxHP = player.MaxHP,
                NewMaxMP = player.MaxMP,

            };

            // 현재 룸의 모든 플레이어에게 브로드캐스트
            if(CurrentRoom != null)
            {
                // async 메서드를 동기적으로 호출(이벤트 핸들러는 void 반환)
                _ = CurrentRoom.BroadcastAsync( packet );
            }

			_logger.LogInformation( "[Event] Player Level Up: PlayerId={PlayerId},NewLevel={NewLevel}, HP={MaxHP}, MP={MaxMP}",
                player.PlayerId, player.Level, player.MaxHP, player.MaxMP);
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
                player.PlayerId, item.ItemId, item.Slot, item.Quantity);
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

            packet.UpdatedStats = new PlayerStats
            {
                Attack = player.GetTotalAttack(),
                Defense = player.GetTotalDefense(),
                MaxHP = player.MaxHP,
                MaxMP = player.MaxMP,
                CurrentHP = player.CurrentHP,
                CurrentMP = player.CurrentMP,
            };

            Send( packet );

			_logger.LogInformation( "[Event] Item Equipped: PlayerId={PlayerId}, Slot={Slot}, ItemId={ItemId}",
                player.PlayerId, slot, item?.ItemId ?? 0);
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

            packet.UpdatedStats = new PlayerStats
            {
                Attack = player.GetTotalAttack(),
                Defense = player.GetTotalDefense(),
                MaxHP = player.MaxHP,
                MaxMP = player.MaxMP,
                CurrentHP = player.CurrentHP,
                CurrentMP = player.CurrentMP,
            };

            Send( packet );

			_logger.LogInformation( "[Event] Item Unequipped: PlayerId={PlayerId}, Slot={Slot}, ReturnedSlot={ReturnedSlot}",
                player.PlayerId, slot, item?.Slot ?? -1);
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
                player.PlayerId, item.ItemId, slot);
		}

        // 장비 스탯 변경 이벤트 핸들러
        private void OnEquipmentStatsChanged(Player player, Dictionary<PlayerEquipment.StatType, int> stats)
        {
            S_PlayerStat packet = new S_PlayerStat
            {
                Player = player.Info
            };

			Send( packet );

			_logger.LogDebug( "[Event] Equipment Stats Changed: PlayerId={PlayerId}",
                player.PlayerId );
		}

        // 플레이어 죽음 이벤트 핸들러
        private void OnPlayerDeath(Player player)
        {
            S_Despawn packet = new S_Despawn
            {
                ObjectIds = {player.PlayerId}
            };

            if(CurrentRoom != null)
            {
                _ = CurrentRoom.BroadcastAsync( packet );
            }

			_logger.LogWarning( "[Event] Player Death: PlayerId={PlayerId}", player.PlayerId );
		}

		// 플레이어 상태 업데이트
		public void UpdatePosition(PosInfo newPosition)
        {
            if(Player == null) return;

            Player.UpdatePosition(newPosition);
        }

        // 플레이어 위치 업데이트 (Room 기반 검증 포함)
        public async Task<bool> UpdatePositionAsync(PosInfo newPosition)
        {
            try
            {
                if(CurrentRoom == null)
                {
                    _logger.LogWarning( "플레이어 {PlayerId}가 룸에 속하지 않은 상태에서 위치 업데이트 시도.", Player.PlayerId );
                    return false;
                }

                // PlayerPositionService를 통해 룸 기반 검증과 함께 위치 업데이트
                bool isValidPosition = await _playerPositionService.UpdatePositionWithValidationAsync(
                    Player.PlayerId, newPosition, (BaseRoom)CurrentRoom);

                if(isValidPosition)
                {
                    // 플레이어 로컬 위치 정보도 업데이트
                    Player.UpdatePosition( newPosition );

                    _logger.LogDebug( "플레이어 위치 업데이트 성공: PlayerId={PlayerId}, Position=({X}, {Y}, {Z})",
                        Player.PlayerId, newPosition.PosX, newPosition.PosY, newPosition.PosZ );
                }
                else
                {
                    _logger.LogWarning( "플레이어 위치 업데이트 실패 (경계 밖): PlayerId={PlayerId}, Position=({X}, {Y}, {Z})",
                        Player.PlayerId, newPosition.PosX, newPosition.PosY, newPosition.PosZ );
                }

                return isValidPosition;
            }
            catch (Exception ex)
            {
                _logger.LogError( ex, "플레이어 위치 업데이트 중 오류 : PlayerId = {PlayerId}", Player.PlayerId );
                return false;
            }
        }

        //현재 플레이어 위치 조회
        public async Task<PosInfo> GetCurrentPositionAsync()
        {
            try
            {
                return await _playerPositionService.GetPositionAsync( Player.PlayerId );
            }
            catch (Exception ex)
            {
                _logger.LogError( ex, "플레이어 위치 조회 중 오류 : PlayerId={PlayerId}", Player.PlayerId );
                return null;
            }
        }

        // 주변 플레이어 조회
        public async Task<List<(long PlayerId, PosInfo Position)>> GetNearbyPlayersAsync(float radius = 100.0f)
        {
            try
            {
                if(CurrentRoom == null)
                    return new List<(long PlayerId, PosInfo Position)>();

                return await _playerPositionService.GetNearbyPlayersInRoomAsync( Player.PlayerId,
                    radius, (BaseRoom)CurrentRoom );
            }
            catch (Exception ex)
            {
                _logger.LogError( ex, "주변 플레이어 조회 중 오류 : PlayerId={PlayerId}", Player.PlayerId );
                return new List<(long PlayerId, PosInfo Position)>();
            }
        }

        public bool TakeDamage( int damage)
        {
            if(Player == null) return false;

            bool result = Player.TakeDamage(damage);
            if(result)
            {
                if(Player.State == PlayerState.Dead)
                {
                    // TODO : 플레이어 Dead 상태 전달 필요.
                }
            }

            return result;
        }

        public bool Heal(int amount)
        {
            if(Player == null) return false;

            return Player.Heal(amount);
        }

        public bool GainExperience(long exp)
        {
            if(Player == null) return false;

            return Player.GainExperience(exp);
        }

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
        public PlayerInfo GetPlayerFullInfo()
        {
            return Player?.Info;
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
