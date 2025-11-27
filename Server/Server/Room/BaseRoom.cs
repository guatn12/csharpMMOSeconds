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
using System.Text;
using System.Threading.Tasks;
using Server.Game;
using Server.Data;
using Server.Utils;
using Server.Core.Jobs;
using Server.Services.Combat;
using Server.Services.Reward;
using Server.Services.DTOs;
using static System.Net.Mime.MediaTypeNames;

namespace Server.Room
{
	public abstract class BaseRoom : IRoom, IDisposable
	{
		protected readonly ILogger _logger;
		protected readonly ConcurrentDictionary<long, GameSession> _players;
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

		public IReadOnlyList<GameSession> Players => _players.Values.ToList();
		public bool IsEmpty => _players.IsEmpty;
		public bool IsFull => MaxPlayers <= _players.Count;

		// 몬스터
		protected MonsterSpawner _monsterSpawner;
		protected readonly DataManager _dataManager;
		private System.Threading.Timer _monsterUpdateTimer;

		private readonly JobQueueManager _jobQueueManager;
		private readonly JobPool _jobPool;
		private bool _isMonsterUpdateScheduled = false;	// Timer 누적 방지

		// Service
		protected readonly ICombatService _combatService;
		protected readonly IRewardService _rewardService;

		public event EventHandler<PlayerRoomEventArgs> PlayerEntered;
		public event EventHandler<PlayerRoomEventArgs> PlayerLeft;

		protected BaseRoom( ILogger logger, string roomName, int maxPlayers, DataManager dataManager,
			JobQueueManager jobQueueManager, JobPool jobPool, ICombatService combatService, IRewardService rewardService,
			float roomWidth = 100.0f, float roomHeight = 50.0f, float roomDepth = 100.0f,
			float minX = 0.0f, float minY = 0.0f, float minZ = 0.0f )
		{
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			_dataManager = dataManager;
			_jobQueueManager = jobQueueManager;
			_jobPool = jobPool;

			_combatService = combatService;
			_rewardService = rewardService;

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

			_players = new ConcurrentDictionary<long, GameSession>();

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
			InitializeMonsterSpawner();

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
			if(_monsterSpawner != null)
			{
				_monsterSpawner.OnMonsterDespawned -= OnMonsterDespawned;
			}

			// 모든 몬스터 제거
			_monsterSpawner?.ClearAllMonsters();
			_monsterSpawner = null;

			// 모든 플레이어 강제 퇴장
			List<GameSession> playersToRemove = _players.Values.ToList();
			foreach(var player in playersToRemove)
			{
				await ForceLeaveAsync( player );
			}

			await OnCleanupAsync();
		}

		public bool ContainsPlayer( GameSession session )
		{
			return session != null && _players.ContainsKey( session.SessionId );
		}

		public bool ContainsPlayerToPlayerId( long playerId )
		{
			return 0 < playerId && _players.Values.Where( p => p.Player.PlayerId == playerId ).Any();
		}

		public GameSession FindPlayer( int sessionId )
		{
			_players.TryGetValue( sessionId, out var session );
			return session;
		}

		public GameSession FindPlayerToPlayerId( long playerId )
		{
			return _players.Values.Where( p => p.Player.PlayerId == playerId ).FirstOrDefault();
		}

		public virtual async Task<RoomEnterResult> TryEnterAsync( GameSession session )
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
				session.CurrentRoom = null; // 실패 시 session의 현재 룸도 초기화.
				_logger.LogError( e, "Failed to enter player {SessionId} to room {RoomId}", session.SessionId, RoomId );
				return RoomEnterResult.UnknownError;
			}
		}

		public virtual async Task<bool> TryLeaveAsync( GameSession session )
		{
			if(session == null || !_players.ContainsKey( session.SessionId ))
				return false;

			return await InternalLeaveAsync( session, false );
		}

		public virtual async Task BroadcastAsync( IMessage packet, GameSession excludeSession = null )
		{
			if(packet == null)
				return;

			List<GameSession> currentPlayers = _players.Values.ToList();
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

		public virtual async Task SendToPlayerAsync( GameSession session, IMessage packet )
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

		public virtual async Task HandlePlayerMoveAsync( GameSession session, Protocol.C_Move packet, ILogger logger )
		{
			if(!ContainsPlayer( session ) || packet?.PosInfo == null)
				return;

			try
			{
				// 이동 검증 (하위 클래스에서 재정의 가능)
				if(!await ValidatePlayerMoveAsync( session, packet ))
				{
					// 검증 실패 시 현재 위치를 클라이언트에 재전송 (동기화)
					PosInfo currentPos = await session.GetCurrentPositionAsync();
					if(currentPos != null)
					{
						var correctionPacket = new Protocol.S_Move
						{
							PlayerId = session.SessionId,
							PosInfo = currentPos
						};
						await SendToPlayerAsync(session, correctionPacket );
						logger.LogWarning("위치 검증 실패로 클라이언트 위치 동기화: Player {SessionId}", session.SessionId);
					}

					return;
				}

				// GameSession을 통해 Redis 기반 3D 위치 업데이트
				bool positionUpdated = await session.UpdatePositionAsync(packet.PosInfo);
				if(!positionUpdated)
				{
					logger.LogWarning( "Player {PlayerId} 위치 업데이트 실패 (경계 밖 또는 오류)", session.SessionId );
					return;
				}

				// 룸별 이동 처리
				await OnPlayerMoveAsync( session, packet );

				// 다른 플레이어들에게 브로드캐스트
				var moveResponse = new Protocol.S_Move
				{
					PlayerId = session.SessionId,
					PosInfo = packet.PosInfo
				};

				await BroadcastAsync( moveResponse, session );

				logger.LogDebug( "Player {SessionId} moved in room {RoomId} to ({X}, {Y}, {Z})",
					session.SessionId, RoomId, packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ );
			}
			catch(Exception e)
			{
				logger.LogError( e, "Failed to handle move for player {SessionId} in room {RoomId}",
					session.SessionId, RoomId );
			}
		}

		public virtual async Task HandlePlayerChatAsync( GameSession session, Protocol.C_Chat packet, ILogger logger )
		{
			if(!ContainsPlayer( session ) || string.IsNullOrWhiteSpace( packet?.Message ))
				return;

			try
			{
				// 채팅 검증 (하위 클래스에서 재정의 가능)
				if(!await ValidatePlayerChatAsync( session, packet ))
					return;

				// 룸별 채팅 처리
				await OnPlayerChatAsync( session, packet );

				// 룸 내 브로드캐스트
				var chatResponse = new Protocol.S_Chat
				{
					PlayerId= session.SessionId,
					Message = packet.Message,
				};

				await BroadcastAsync( chatResponse, session );

				logger.LogDebug( "Player {SessionId} chatted in room {RoomId}: '{Message}'",
					session.SessionId, RoomId, packet.Message );
			}
			catch(Exception ex)
			{
				logger.LogError( ex, "Failed to handle chat for player {SessionId} in room {RoomId}",
					session.SessionId, RoomId );
			}
		}

		public virtual async Task HandlePlayerPlayerInfoAsync( GameSession session, C_PlayerInfo packet, ILogger logger )
		{
			if(!ContainsPlayer( session ) || packet?.TargetPlayerId < 0 ||
				!ContainsPlayerToPlayerId( packet.TargetPlayerId ))
				return;

			try
			{
				// 플레이어 정보를 찾는데 검증이 필요할지 의문.
				if(!await ValidatePlayerInfoAsync( session, packet ))
					return;

				GameSession targetSession = FindPlayerToPlayerId(packet.TargetPlayerId);
				if(targetSession == null)
				{
					logger.LogWarning( "TargetPlayer {PlayerId} Not Found int Room {RoomId}",
						packet.TargetPlayerId, RoomId );

					Protocol.S_PlayerStat errorResponse = new Protocol.S_PlayerStat();
					await SendToPlayerAsync( session, errorResponse );
					return;
				}

				// 데이터 탐색 및 전달.
				Protocol.S_PlayerStat response = new Protocol.S_PlayerStat
				{
					Player = targetSession.Player.Info
				};

				await SendToPlayerAsync( session, response );

				logger.LogDebug( "TargetPlayer (Id: {PlayerId}, Name: {PlayerName}) Find in Room {RoomId}",
					targetSession.Player.PlayerId, targetSession.Player.PlayerName, RoomId );
			}
			catch(Exception e)
			{
				logger.LogError( e, "Failed to handle PlayerInfo Find player {TargetPlayerId} in room {RoomId}",
					packet.TargetPlayerId, RoomId );
			}
		}

		public virtual async Task HandlePlayerUseSkillAsync( GameSession session, C_UseSkill packet, ILogger logger )
		{
			if(!ContainsPlayer( session ) || !ContainsPlayerToPlayerId( packet.TargetId ))
				return;

			try
			{
				// 스킬 사용 검증 (하위 클래스에서 재정의 가능)
				if(!await ValidatePlayerUseSkillAsync( session, packet ))
					return;

				// 스킬 사용 실행
				bool skillUsed = session.Player.UseSkill(packet.SkillId);
				if(!skillUsed)
				{
					logger.LogWarning( "Player {PlayerId} failed to use skill {SkillId} - insufficient resources or invalid state",
						session.Player.PlayerId, packet.SkillId );
					return;
				}

				// 룸별 스킬 사용 처리. - 스킬 사용만으로 힐, 데미지 버프등을 판단하여 브로드캐스트 할 수 없음.
				// 내부에서 판단하여 주변 유저들에게 전파해야 한다.
				await OnPlayerUseSkillAsync( session, packet );

				logger.LogDebug( "Player {PlayerId} Use Skill ({SkillId}) Success, To Target Player {PlayerId} In Room {RoomId}",
					session.Player.PlayerId, packet.SkillId, packet.TargetId, RoomId );
			}
			catch(Exception e)
			{
				logger.LogError( e, "Failed to Use Skill (PlayerId: {PlayerId}, SkillId: {SkillId}, TargetId: {TargetId}) in room {RoomId}",
					session.Player.PlayerId, packet.SkillId, packet.TargetId, RoomId );
			}
		}

		public virtual async Task HandlePlayerInventoryRequestAsync(GameSession session, C_InventoryRequest packet, ILogger logger )
		{
			if(!ContainsPlayer( session ) || packet == null)
				return;

			try
			{
				// 플레이어 인벤토리 데이터 조회
				InventoryModel inventoryData = session.Player.GetInventoryData();
				var equipmentData = session.Player.GetEquipmentData();

				// 응답 패킷 생성
				var response = new S_InventoryData
				{
					Gold = session.Player.GetGold(),
					MaxSlots = 50
				};

				// 인벤토리 아이템들 추가
				foreach(var item in inventoryData.Items)
				{
					var itemInfo = new InventoryItemInfo
					{
						ItemId = item.ItemId,
						Quantity = item.Quantity,
						Slot = item.Slot,
						EnhancementLevel = item.Enhancement?.Level ?? 0,
						CustomName = item.CustomName ?? string.Empty,
						AcquiredAt = item.AcquiredAt?.Ticks ?? 0
					};

					// Options 복사
					if(item.Options != null)
					{
						foreach(var option in item.Options)
						{
							itemInfo.Options[ option.Key ] = option.Value;
						}
					}

					response.Items.Add( itemInfo );
				}

				await SendToPlayerAsync( session, response );

				logger.LogDebug( "Sent inventory data to Player {SessionId} in Room{RoomId}: {ItemCount} items, {Gold} gold",
					session.SessionId, RoomId, response.Items.Count, response.Gold);
			}
			catch ( Exception ex )
			{
				logger.LogError( ex, "Failed to handle inventory request for Player {SessionId} in Room {RoomId}",
					session.SessionId, RoomId);

				// 에러 응답 전송
				var errorResponse = new S_InventoryData{Gold = 0, MaxSlots = 50};
				await SendToPlayerAsync( session, errorResponse );
			}
		}

		public virtual async Task HandlePlayerUseItemAsync(GameSession session, C_UseItem packet, ILogger logger)
		{
			if(!ContainsPlayer( session ) || packet == null ||
				packet.Slot < 0 || 50 <= packet.Slot || packet.Quantity <= 0)
				return;

			try
			{
				// 아이템 사용 전 상태 저장
				int oldHP = session.Player.CurrentHP;
				int oldMP = session.Player.CurrentMP;
				InventoryItem item = session.Player.Inventory.GetItem(packet.Slot);

				bool success = session.Player.UseItem(packet.Slot, packet.Quantity);

				var response = new S_UseItem
				{
					Success = success,
					Slot = packet.Slot,
					RemainingQuantity = success ? (item?.Quantity ?? 0) : 0,
					Message = success ? "아이템을 사용했습니다." : "아이템 사용에 실패했습니다."
				};

				await SendToPlayerAsync( session, response );

				if(success)
				{
					// HP/MP 변화가 있으면 플레이어 상태 업데이트 브로드캐스트
					if(oldHP != session.Player.CurrentHP || oldMP != session.Player.CurrentMP)
					{
						var updatePacket = new S_PlayerUpdate
						{
							Player = session.Player.Info
						};
						await BroadcastAsync( updatePacket, session );
					}

					logger.LogInformation("Player {SessionId} used item from slot{Slot}x{Quantity} in Room {RoomId}",
						session.SessionId, packet.Slot, packet.Quantity, RoomId);
				}
				else
				{
					 logger.LogWarning("Player {SessionId} failed to use item from slot {Slot}in Room {RoomId}",
						 session.SessionId, packet.Slot, RoomId);
				}
			}
			catch (Exception ex)
			{
				 logger.LogError(ex, "Failed to handle use item for Player {SessionId} in Room{RoomId}",
					 session.SessionId, RoomId);

				var eerrorResponse = new S_UseItem
				{
					Success = false,
					Message = "서버 오류가 발생했습니다."
				};
				await SendToPlayerAsync ( session, eerrorResponse );
			}
		}

		public virtual async Task HandlePlayerEquipItemAsync(GameSession session, C_EquipItem packet, ILogger logger)
		{
			if(!ContainsPlayer( session ) || packet == null || packet.InventorySlot < 0 || 50 <= packet.InventorySlot ||
				packet.EquipSlot < 0 || 10 < packet.EquipSlot)
				return;

			try
			{
				// 장비 착용 전 스탯 저장
				int oldAttack = session.Player.GetTotalAttack();
				int oldDefense = session.Player.GetTotalDefense();
				int oldMaxHP = session.Player.MaxHP;
				int oldMaxMP = session.Player.MaxMP;

				bool success = session.Player.EquipItemFromInventory(packet.InventorySlot);

				// 응답 패킷 생성
				var response = new S_ItemEquipped
				{
					Success = success,
					InventorySlot = packet.InventorySlot,
					EquipSlot = packet.EquipSlot,
				};

				if(success )
				{
					// 장착된 장비 정보 업데이트
					var equipmentData = session.Player.GetEquipmentData();
					response.UpdatedEquipment = new EquipmentInfo();

					foreach(var kvp in equipmentData)
					{
						switch(kvp.Key)
						{
						case PlayerEquipment.EquipSlot.Weapon:
							response.UpdatedEquipment.WeaponItemId = kvp.Value.ItemId;
							break;
						case PlayerEquipment.EquipSlot.Armor:
							response.UpdatedEquipment.ArmorItemId = kvp.Value.ItemId;
							break;
						case PlayerEquipment.EquipSlot.Helmet:
							response.UpdatedEquipment.HelmetItemId = kvp.Value.ItemId;
							break;
						case PlayerEquipment.EquipSlot.Gloves:
							response.UpdatedEquipment.GlovesItemId = kvp.Value.ItemId;
							break;
						}
					}

					response.UpdatedStats = new PlayerStats
					{
						Attack = session.Player.GetTotalAttack(),
						Defense = session.Player.GetTotalDefense(),
						MaxHP = session.Player.MaxHP,
						MaxMP = session.Player.MaxMP,
						CurrentHP = session.Player.CurrentHP,
						CurrentMP = session.Player.CurrentMP
					};

					// 스탯 변화가 있으면 플레이어 상태 업데이트 브로드캐스트?
					//if(oldAttack != session.Player.GetTotalAttack() || oldDefense != session.Player.GetTotalDefense() || 
					//	oldMaxHP != session.Player.MaxHP || oldMaxMP != session.Player.MaxMP)
					//{
					//	var updatePacket = new S_PlayerUpdate
					//	{
					//		Player = session.Player.Info
					//	};
					//	await BroadcastAsync( updatePacket, session );
					//}

					 logger.LogInformation("Player {SessionId} equipped item from slot {Slot}to equipment slot {EquipSlot} in Room {RoomId}",
						 session.SessionId, packet.InventorySlot, packet.EquipSlot, RoomId);
				}
				else
				{
					logger.LogWarning("Player {SessionId} failed to equip item from slot{Slot} in Room {RoomId}",
						session.SessionId, packet.InventorySlot, RoomId);
				}

				await SendToPlayerAsync( session, response );
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to handle equip item for Player {SessionId} in Room {RoomId}",
					session.SessionId, RoomId);
				
				var errorResponse = new S_ItemEquipped { Success = false };
				await SendToPlayerAsync(session, errorResponse);
			}
		}

		public virtual async Task HandlePlayerUnequipItemAsync(GameSession session, C_UnequipItem packet,  ILogger logger)
		{
			if(!ContainsPlayer( session ) || packet == null || packet.EquipSlot < 1 || 10 < packet.EquipSlot)
				return;

			try
			{
				// 장비 착용 전 스탯 저장
				int oldAttack = session.Player.GetTotalAttack();
				int oldDefense = session.Player.GetTotalDefense();
				int oldMaxHP = session.Player.MaxHP;
				int oldMaxMP = session.Player.MaxMP;

				var equipSlot = (PlayerEquipment.EquipSlot)packet.EquipSlot;
				bool success = session.Player.UnequipItemToInventory(equipSlot);

				var response = new S_ItemUnequipped
				{
					Success = success,
					EquipSlot = packet.EquipSlot,
					ReturnedToSlot = success ? GetLastInventorySlot(session.Player) : -1
				};

				if(success)
				{
					// 장비 해제 후 정보 업데이트
					var equipmentData = session.Player.GetEquipmentData();
					response.UpdatedEquipment = new EquipmentInfo();

					foreach(var kvp in equipmentData)
					{
						switch(kvp.Key)
						{
						case PlayerEquipment.EquipSlot.Weapon:
							response.UpdatedEquipment.WeaponItemId = kvp.Value.ItemId;
							break;
						case PlayerEquipment.EquipSlot.Armor:
							response.UpdatedEquipment.ArmorItemId = kvp.Value.ItemId;
							break;
						case PlayerEquipment.EquipSlot.Helmet:
							response.UpdatedEquipment.HelmetItemId = kvp.Value.ItemId;
							break;
						case PlayerEquipment.EquipSlot.Gloves:
							response.UpdatedEquipment.GlovesItemId = kvp.Value.ItemId;
							break;
						}
					}

					response.UpdatedStats = new PlayerStats
					{
						Attack = session.Player.GetTotalAttack(),
						Defense = session.Player.GetTotalDefense(),
						MaxHP = session.Player.MaxHP,
						MaxMP = session.Player.MaxMP,
						CurrentHP = session.Player.CurrentHP,
						CurrentMP = session.Player.CurrentMP,
					};

					// 스탯 변화가 있으면 플레이어 상태 업데이트 브로드캐스트
					if(oldAttack != session.Player.GetTotalAttack() || oldDefense != session.Player.GetTotalDefense() ||
						oldMaxHP != session.Player.MaxHP || oldMaxMP != session.Player.MaxMP)
					{
						var updatePacket = new S_PlayerUpdate
						{
							Player = session.Player.Info
						};
						await BroadcastAsync( updatePacket, session );
					}

					logger.LogInformation( "Player {SessionId} unequipped item from slot{Slot} in Room {RoomId}",
						session.SessionId, packet.EquipSlot, RoomId);
				}
				else
				{
					logger.LogWarning("Player {SessionId} failed to unequip item from slot{Slot} in Room {RoomId}",
						session.SessionId, packet.EquipSlot, RoomId);
				}

				await SendToPlayerAsync( session, response );
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to handle unequip item for Player {SessionId} inRoom {RoomId}",
					session.SessionId, RoomId);
				
				var errorResponse = new S_ItemUnequipped { Success = false };
				await SendToPlayerAsync(session, errorResponse);
			}
		}

		// 플레이어가 몬스터 공격 핸들러 추가.
		public virtual async Task HandlePlayerAttackMonsterAsync( GameSession session, Protocol.C_AttackMonster packet, ILogger logger )
		{
			if(!ContainsPlayer( session ) || packet == null)
				return;

			try
			{
				// 몬스터 존재 확인
				Monster monster = _monsterSpawner?.GetMonster(packet.MonsterId);
				if(monster == null || !monster.IsAlive)
				{
					_logger.LogWarning( "Player {PlayerId} tried to attack non-existent or dead monster {MonsterId}",
						session.Player.PlayerId, packet.MonsterId );
					return;
				}

				var result = await _combatService.ProcessPlayerAttackMonsterAsync(session.Player, monster);
				if(result == null)
				{
					_logger.LogWarning( "Player {PlayerId} tried to attack non-existent or dead monster {MonsterId}",
						session.Player.PlayerId, monster.MonsterId );
					return;
				}

				// 공격 쿨다운
				session.Player.StartAttackCooldown();

				logger.LogInformation( "Player {PlayerId} attacked Monster {MonsterId} for {Damage} damage( Critical: {IsCritical}) -remain Monster HP({CurrentHP})",
					session.Player.PlayerId, packet.MonsterId, result.Damage, result.IsCritical, result.TargetCurrentHP);

				// s_damage 패킷 브로드캐스트
				var damagePacket = new S_Damage
				{
					AttackerId = result.AttackerId,
					TargetId = result.TargetId,
					Damage = result.Damage,
					CurrentHP = result.TargetCurrentHP,
				};
				await BroadcastAsync( damagePacket );

				// 몬스터 상태 업데이트 브로드 캐스트
				var updatePacket = new S_MonsterUpdate
				{
					Monsters = {monster.Info }
				};
				await BroadcastAsync( updatePacket );

				// 몬스터 사망 처리
				if(result.TargetDied)
				{
					await HandleMonsterDeathAsync(monster, session.Player.PlayerId);
				}
			}
			catch (Exception ex)
			{
				logger.LogError( ex, "Failed to handle attack monster for Player {PlayerId} in Room {RoomId}",
					session.Player.PlayerId, RoomId );
			}
		}

		protected virtual async Task HandleMonsterDeathAsync(Monster monster, long killerPlayerId)
		{
			try
			{
				// 킬러 플레이어 찾기
				GameSession killerSession = FindPlayerToPlayerId(killerPlayerId);
				if(killerSession == null)
				{
					_logger.LogWarning( "Killer player {PlayerId} not found in room", killerPlayerId );
					return;
				}

				// 보상
				var reward = await _rewardService.CalculateMonsterRewardAsync(killerSession.Player, monster);
				await _rewardService.GiveRewardAsync( killerSession.Player, reward );

				// S_MonsterDie 브로드캐스트
				var diePacket = new Protocol.S_MonsterDie
				{
					MonsterId = monster.MonsterId,
					KillPlayerId = killerPlayerId,
					ExpGained = reward.Experience,
					GoldGained = reward.Gold,
				};
				diePacket.DroppedItems.AddRange( reward.DroppedItem );
				await BroadcastAsync( diePacket );

				// 레벨업 처리
				if(reward.LeveledUp)
				{
					var levelUpPacket = new S_LevelUp
					{
						PlayerId = killerPlayerId,
						NewLevel = reward.NewLevel,
						NewMaxHP = reward.NewMaxHP,
						NewMaxMP = reward.NewMaxMP,
					};
					await BroadcastAsync( levelUpPacket );

					_logger.LogInformation( "Player {PlayerId} leveled up to {Level}",
						killerPlayerId, reward.NewLevel );
				}

				_logger.LogInformation( "Monster {MonsterId} ({Name}) killed by Player {PlayerId}. Rewards: {Exp} exp, {Gold} gold",
					monster.MonsterId, monster.Name, killerPlayerId, reward.Experience, reward.Gold );

				_monsterSpawner.ScheduleDespawn( monster.MonsterId, TimeSpan.FromSeconds( 5 ) );
			}
			catch (Exception ex)
			{
				_logger.LogError( ex, "Failed to handle monster death for Monster {MonsterId} in Room {RoomId}",
					monster.MonsterId, RoomId );
			}
		}

		protected virtual Task OnInitializeAsync() => Task.CompletedTask;
		protected virtual Task OnCleanupAsync() => Task.CompletedTask;
		protected virtual async Task OnPlayerEnterAsync( GameSession session )
		{
			// 현재 스폰된 몬스터 정보 전송
			if(_monsterSpawner != null)
			{
				var aliveMonsters = _monsterSpawner.GetAliveMonsters();
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
		protected virtual Task OnPlayerLeaveAsync( GameSession session ) => Task.CompletedTask;
		protected virtual Task OnPlayerMoveAsync( GameSession session, Protocol.C_Move packet ) => Task.CompletedTask;
		protected virtual Task OnPlayerChatAsync( GameSession session, Protocol.C_Chat packet ) => Task.CompletedTask;
		protected virtual Task OnPlayerUseSkillAsync( GameSession session, Protocol.C_UseSkill packet ) => Task.CompletedTask;

		protected virtual Task<bool> ValidatePlayerMoveAsync( GameSession session, Protocol.C_Move packet )
		{
			// 기본 3D 위치 검증
			bool isValid = Utils.Position3DValidator.IsValidPosition(packet.PosInfo, this);

			if(!isValid)
			{
				_logger.LogWarning( "Invalid move attempt by player {SessionId} in room{ RoomId}: Position ({X}, {Y}, {Z}) is outside room bounds ({MinX}-{MaxX},{ MinY}-{ MaxY}, { MinZ}-{ MaxZ})",
					session.SessionId, RoomId,packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ,
					MinX, MaxX, MinY, MaxY, MinZ, MaxZ );
			}

			return Task.FromResult( isValid );
		}
		protected virtual Task<bool> ValidatePlayerChatAsync( GameSession session, Protocol.C_Chat packet ) => Task.FromResult( true );
		protected virtual Task<bool> ValidatePlayerInfoAsync( GameSession session, Protocol.C_PlayerInfo packet ) => Task.FromResult( true );
		protected virtual Task<bool> ValidatePlayerUseSkillAsync( GameSession session, Protocol.C_UseSkill packet ) => Task.FromResult( true );

		// 몬스터 초기화 메서드 추가
		protected virtual void InitializeMonsterSpawner()
		{
			_monsterSpawner = new MonsterSpawner( this, _dataManager, _logger );

			// Despawn 이벤트 구독 (JobQueue에서 실행됨)
			_monsterSpawner.OnMonsterDespawned += OnMonsterDespawned;

			// Spawn 이벤트 구독 (JobQueue에서 실행됨)
			_monsterSpawner.OnMonsterSpawned += OnMonsterSpawned;

			// 기본 스폰 포인트 설정(하위 클래스에서 재정의 가능)
			SetupDefaultSpawnPoints();

			// 초기 몬스터 스폰
			_monsterSpawner.SpawnInitialMonsters();

			// 주기적 업데이트 타이머 시작 (100ms마다)
			_monsterUpdateTimer = new System.Threading.Timer(
				callback: _ => UpdateMonsters(),
				state: null,
				dueTime: TimeSpan.FromMilliseconds( 100 ),
				period: TimeSpan.FromMilliseconds( 100 ) );

			_logger.LogInformation( "Monster spawner initialized for room {RoomId}", RoomId );
		}

		/// <summary>
		/// MonsterSpawner에서 딜레이 Despawn이 완료되었을 때 호출됨
		/// JobQueue Worker 스레드에서 실행되므로 스레드 안전
		/// </summary>
		private void OnMonsterDespawned(long monsterId)
		{
			try
			{
				// S_MonsterDespawn 브로드캐스트
				S_MonsterDespawn despawnPacket = new S_MonsterDespawn();
				despawnPacket.MonsterIds.Add( monsterId );

				// async 메서드를 동기적으로 실행 (JobQueue 안에서)
				BroadcastAsync(despawnPacket).GetAwaiter().GetResult();

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
		private void OnMonsterSpawned(Monster monster)
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

			_monsterSpawner.AddSpawnPoint( 2201, new PosInfo
			{
				PosX = centerX - 5,
				PosY = centerY,
				PosZ = centerZ
			} );

			_monsterSpawner.AddSpawnPoint( 2201, new PosInfo
			{
				PosX = centerX + 5,
				PosY = centerY,
				PosZ = centerZ
			} );

			_monsterSpawner.AddSpawnPoint( 2001, new PosInfo
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

			if(_monsterSpawner == null)
			{
				_logger.LogWarning( "MonsterSpawner is null for room {RoomId}", RoomId );
				return;
			}

			_isMonsterUpdateScheduled = true;

			// MonsterUpdateJob 생성 및 초기화
			MonsterUpdateJob job = _jobPool.Get<MonsterUpdateJob>();
			job.Initialize( _monsterSpawner, RoomId, _logger );

			// JobQueue에 비동기 추가
			_ = _jobQueueManager.PushAsync( job )
				.AsTask()
				.ContinueWith( t =>
				{
					// Job 큐잉 완료 후 플래그 해제
					_isMonsterUpdateScheduled = false;

					if(t.IsFaulted)
					{
						_logger.LogError( t.Exception, "Failed to push MonsterUpdateJob to queue for room {RoomId}", RoomId );
					}
				}, TaskScheduler.Default );
		}

		

		private static int GenerateNextRoomId()
		{
			return System.Threading.Interlocked.Increment( ref _nextRoomId );
		}

		private async Task<bool> ForceLeaveAsync( GameSession session )
		{
			return await InternalLeaveAsync( session, true );
		}

		private async Task<bool> InternalLeaveAsync( GameSession session, bool isForced )
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

		private int GetLastInventorySlot(Player player )
		{
			InventoryModel inventoryData = player.GetInventoryData();
			return inventoryData.Items.LastOrDefault()?.Slot ?? -1;
		}

		private bool ValidateSession(GameSession session, ILogger logger)
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
					packet?.Slot ?? -1, packet?.Quantity ?? -1);
				return false;
			}
			return true;
		}

		public void Dispose()
		{
			Dispose( true );
			GC.SuppressFinalize( this );
		}

		public Monster GetMonster(long monsterId)
		{
			return _monsterSpawner?.GetMonster(monsterId);
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
