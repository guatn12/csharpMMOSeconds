using DummyClient.Extensions;
using Microsoft.Extensions.Logging; // Added for ILogger
using Protocol; // Added for packet types
using ServerCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DummyClient.Packet
{
	public class ClientPacketHandler : BaseClientPacketHandler
	{
		private readonly ILogger<ClientPacketHandler> _logger;

		public ClientPacketHandler( ILogger<ClientPacketHandler> logger )
		{
			_logger = logger;
		}

		public override ValueTask On_S_EnterGame( NetworkSession session, S_EnterGame packet )
		{
			var ctx = ((ServerSession)session).Context;
			_logger.LogInformation( "[Client] S_EnterGame - PlayerId: {PlayerId}, " +
				"PlayerName: {PlayerName}, Level: {Level}, HP: {HP}/{MaxHP}",
				packet.Player.ObjectId, packet.Player.Name,
				packet.Player.StatInfo.Level, packet.Player.StatInfo.CurrentHP, packet.Player.StatInfo.MaxHP );

			// 플레이어 초기화.
			ctx.MyPlayer.PlayerId = packet.Player.ObjectId;
			ctx.MyPlayer.PlayerName = packet.Player.Name;
			ctx.MyPlayer.CurrentMapId = packet.MapId;
			ctx.MyPlayer.Stats = packet.Player.StatInfo.Clone();

			// 위치 정보 초기화
			if(packet.Player.PosInfo != null)
			{
				ctx.MyPlayer.Position = packet.Player.PosInfo.Clone();
			}

			// 맵 데이터 로드
			ctx.CurrentMapData = Program.DataManagerInstance.GetMap( packet.MapId );
			if(ctx.CurrentMapData != null)
			{
				_logger.LogInformation( "현재 맵: {MapName} (ID: {MapId})",
					ctx.CurrentMapData.Name, ctx.CurrentMapData.Id );
			}
			else
			{
				_logger.LogWarning( "맵 데이터 없음. MapId: {MapId}", packet.MapId );
			}

			// 데이터 초기화 - 타겟 몬스터 초기화
			ctx.TargetMonsterId = 0;

			// 전체 출력
			ctx.MyPlayer.LogStatus( _logger );

			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_LeaveGame( NetworkSession session, S_LeaveGame packet )
		{
			var ctx = ((ServerSession)session).Context;
			_logger.LogInformation( "[Client] Received S_LeaveGame. PlayerId: {ObjectId}", packet.ObjectId );

			ctx.MyPlayer.Clear();
			ctx.CurrentMapData = null;
			ctx.NearbyObjects.Clear();
			
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_ChangeRoom( NetworkSession session, S_ChangeRoom packet )
		{
			var ctx = ((ServerSession)session).Context;

			if(packet.Success)
			{
				_logger.LogInformation( $"[Client] 방 이동 성공 - mapId={packet.MapId}" );
				
				// 플레이어 정보 초기화.
				ctx.MyPlayer = packet.Player.ToClientPlayerInfo();
				ctx.MyPlayer.CurrentMapId = packet.MapId;

				// 맵 데이터 로드
				ctx.CurrentMapData = Program.DataManagerInstance.GetMap( packet.MapId );
				if(ctx.CurrentMapData != null)
				{
					_logger.LogInformation( "현재 맵: {MapName} (ID: {MapId})",
						ctx.CurrentMapData.Name, ctx.CurrentMapData.Id );
				}
				else
				{
					_logger.LogWarning( "맵 데이터 없음. MapId: {MapId}", packet.MapId );
				}

				// 데이터 초기화 - 타겟 몬스터 초기화
				ctx.TargetMonsterId = 0;

				// 전체 출력
				ctx.MyPlayer.LogStatus( _logger );
			}
			else
			{
				_logger.LogWarning($"[Client] 방 이동 실패. Reason: {packet.FailReason}");
			}

			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_Spawn( NetworkSession session, S_Spawn packet )
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogInformation( "[Client] S_Spawn - {ObjectCount} Object spawned", packet.Objects.Count );
			foreach(var objectInfo in packet.Objects)
			{
				switch(objectInfo.Type)
				{
				// 오브젝트 타입 추가시 여기도 추가 필요.
				case ObjectType.ObjectPlayer:
				case ObjectType.ObjectMonster:
					if(ctx.NearbyObjects.TryAdd( objectInfo.ObjectId, objectInfo ) == false )
					{
						_logger.LogWarning( "Object Add Fail From On_S_Spawn, ObjectId : {ObjectId}", objectInfo.ObjectId );
					}
					else
					{
						_logger.LogInformation( "Object Spawned: ID {ObjectId}, Name: {Name}, Level: {Level}",
							objectInfo.ObjectId, objectInfo.Name, objectInfo.StatInfo.Level );
					}
					break;
				case ObjectType.ObjectNone:
					_logger.LogWarning( "Unknown Object Type: ObjectNone" );
					break;
				default:
					_logger.LogWarning( "Unknown Object Type: {ObjectType}", objectInfo.Type );
					break;
				}
			}
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_Despawn( NetworkSession session, S_Despawn packet )
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogInformation( "[Client] Received S_Despawn. ObjectCount: {ObjectCount}", packet.Objects.Count );

			foreach(var objectInfo in packet.Objects)
			{
				switch(objectInfo.Type)
				{
					case ObjectType.ObjectPlayer:
					case ObjectType.ObjectMonster:
						if(ctx.NearbyObjects.Remove( objectInfo.ObjectId ))
						{
							_logger.LogInformation( "Object Despawned: ID {ObjectId}, Name: {Name}",
								objectInfo.ObjectId, objectInfo.Name );
						}
						else
						{
							_logger.LogWarning( "Object Despawn Failed: ID {ObjectId} Not Found",
								objectInfo.ObjectId );
						}
					break;
					case ObjectType.ObjectNone:
						_logger.LogWarning( "Unknown Object Type: ObjectNone" );
					break;
					default:
						_logger.LogWarning( "Unknown Object Type: {ObjectType}", objectInfo.Type );
					break;
				}
			}

			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_Move( NetworkSession session, S_Move packet )
		{
			var ctx = ((ServerSession)session).Context;

			foreach(var obj in packet.Objects)
			{
				switch(obj.Type)
				{
					case ObjectType.ObjectPlayer:
					case ObjectType.ObjectMonster:
						if(obj.ObjectId == ctx.MyPlayer.PlayerId)
						{
							// 내 위치 정보 업데이트
							ctx.MyPlayer.Position = obj.PosInfo.Clone();
							_logger.LogInformation( "[Client] S_Move - MyPlayer moved to ({PosX:F1}, {PosY:F1}, {PosZ:F1})",
								obj.PosInfo.PosX, obj.PosInfo.PosY, obj.PosInfo.PosZ );
							break;
						}
						else
						{
							if(ctx.NearbyObjects.TryGetValue( obj.ObjectId, out var objectInfo))
							{
								objectInfo.PosInfo = obj.PosInfo.Clone();
								_logger.LogInformation( "[Client] S_Move - Object({Type}) {ObjectId} ({Name}) moved to ({PosX:F1}, {PosY:F1}, {PosZ:F1})",
									obj.Type, obj.ObjectId, objectInfo.Name, objectInfo.PosInfo.PosX, objectInfo.PosInfo.PosY, objectInfo.PosInfo.PosZ );
							}
						}
						break;
				}
			}

			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_Chat( NetworkSession session, S_Chat packet )
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogInformation( "[Client] Received S_Chat. PlayerId: {PlayerId}, Message: {Message}", 
				packet.PlayerId, packet.Message );
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_PlayerUpdate( NetworkSession session, S_PlayerUpdate packet )
		{
			var ctx = ((ServerSession)session).Context;

			// 내 플레이어 정보만 업데이트
			if(packet.Player.ObjectId == ctx.MyPlayer.PlayerId)
			{
				int oldHP = ctx.MyPlayer.Stats.CurrentHP;
				int oldMP = ctx.MyPlayer.Stats.CurrentMP;

				ctx.MyPlayer.Stats.CurrentMP = packet.Player.StatInfo.CurrentMP;
				ctx.MyPlayer.Stats.CurrentHP = packet.Player.StatInfo.CurrentHP;

				// HP 변화 로그
				if(oldHP != packet.Player.StatInfo.CurrentHP)
				{
					int hpChange = packet.Player.StatInfo.CurrentHP - oldHP;
					string changeStr = 0 < hpChange ? $"+{hpChange}" : hpChange.ToString();

					_logger.LogInformation( "HP : {OldHP} -> {NewHP} ({Change}) [{Percent:F1}%]",
						oldHP, packet.Player.StatInfo.CurrentHP, changeStr, ctx.MyPlayer.HPPercent );

					// HP 위험 경고 (30% 이하)
					if(ctx.MyPlayer.HPPercent < 30f && 30f <= ((float)oldHP / packet.Player.StatInfo.MaxHP * 100))
					{
						_logger.LogWarning( "HP 위험! 포션 사용 권장" );
					}
				}

				// MP 변화 로그
				if(oldMP != packet.Player.StatInfo.CurrentMP)
				{
					int mpChange = packet.Player.StatInfo.CurrentMP - oldMP;
					string changeStr = 0 < mpChange ? $"+{mpChange}" : mpChange.ToString();

					_logger.LogDebug( "MP: {OldMP} -> {NewMP} ({Change})",
						oldMP, packet.Player.StatInfo.CurrentMP, changeStr );
				}
			}

			return ValueTask.CompletedTask;
		}
		public override ValueTask On_S_PlayerStat( NetworkSession session, S_PlayerStat packet )
		{
			var ctx = ((ServerSession)session).Context;

			if(packet.Player.ObjectId == ctx.MyPlayer.PlayerId)
			{
				ctx.MyPlayer.Stats = packet.Player.StatInfo.Clone();

				// 전체 출력
				ctx.MyPlayer.LogStatus( _logger );
			}

			return ValueTask.CompletedTask;
		}
		public override ValueTask On_S_Damage( NetworkSession session, S_Damage packet )
		{
			var ctx = ((ServerSession)session).Context;

			// 공격자와 피해자 정보 파악
			string attackerName = packet.Attacker.Type == ObjectType.ObjectPlayer
				? $"Player {packet.Attacker.ObjectId}"
				: (ctx.NearbyObjects.TryGetValue(packet.Attacker.ObjectId, out var attacker)
					? attacker.Name
					: $"Monster {packet.Attacker.ObjectId}");

			// 몬스터의 경우 (임시 구분)
			foreach(var target in packet.Targets)
			{
				switch(target.Type)
				{
				case ObjectType.ObjectNone:
					_logger.LogWarning( "[Client] Target Type is ObjectNone. Skipping..." );
					continue;
				case ObjectType.ObjectPlayer:
				case ObjectType.ObjectMonster:
					// 타겟이 나인 경우
					if(target.ObjectId == ctx.MyPlayer.PlayerId)
					{
						ctx.MyPlayer.Stats.CurrentHP = target.CurrentHP;
						string playerCriticalStr = target.IsCritical ? " [CRITICAL!]" : "";
						_logger.LogWarning( "[Client] 피격! {Attacker} -> 나 | Damage: {Damage}{Critical} | Remaining HP:{CurrentHP}/{MaxHP} ({Percent:F1}%)",
							attackerName, target.Damage, playerCriticalStr, target.CurrentHP, ctx.MyPlayer.Stats.MaxHP, ctx.MyPlayer.HPPercent );
						// HP 위험 경고
						if(ctx.MyPlayer.HPPercent < 30f)
						{
							_logger.LogError( "HP 위험! 포션 사용 또는 도망 필요!" );
						}
					}
					else
					{
						if(!ctx.NearbyObjects.TryGetValue( target.ObjectId, out var objectInfo ))
						{
							_logger.LogWarning( "[Client] Target Object {TargetId} Not Found From NearbyObjects", target.ObjectId );
							continue;
						}
						objectInfo.StatInfo.CurrentHP = target.CurrentHP;
						string criticalStr = target.IsCritical ? " [CRITICAL!]" : "";
						_logger.LogInformation( "[Client] Damage: {Attacker} -> {Target} | Damage: {Damage}{Critical} | Remaining HP: {CurrentHP}",
							attackerName, objectInfo.Name, target.Damage, criticalStr, target.CurrentHP );
					}
					break;
				default:
					_logger.LogWarning( "[Client] Target Type is Unknown{Type}. Skipping...", target.Type );
					continue;
				}
			}

			return ValueTask.CompletedTask;
		}
		public override ValueTask On_S_Heal( NetworkSession session, S_Heal packet ) 
		{
			var ctx = ((ServerSession)session).Context;

			if(packet.Healer.Damage <= 0)
			{
				_logger.LogWarning( "[Client] S_Heal - HealAmount({HealAmount}) is Zero or Negative!!!", packet.Healer.Damage );
				return ValueTask.CompletedTask;
			}

			foreach(var target in packet.Targets)
			{
				// 힐 타겟이 나인 경우 HP 업데이트
				if(target.ObjectId == ctx.MyPlayer.PlayerId)
				{
					int oldHP = ctx.MyPlayer.Stats.CurrentHP;
					ctx.MyPlayer.Stats.CurrentHP = target.CurrentHP;

					_logger.LogInformation( "[Client] Heal!!! healerId: {healerId} -> TargetId: {TargetId}", packet.Healer.ObjectId, target.ObjectId );
					_logger.LogInformation( "HealAmount: {healAmount} | oldHP: {oldHP} -> CurrentHP: {CurrentHP} (ChangeValue: {Change}) ",
					packet.Healer.Damage, oldHP, target.CurrentHP, target.CurrentHP - oldHP );
					continue;
				}

				// 다른 플레이어 힐
				if(ctx.NearbyObjects.TryGetValue( target.ObjectId, out var targetInfo ))
				{
					int oldHP = targetInfo.StatInfo.CurrentHP;
					targetInfo.StatInfo.CurrentHP = target.CurrentHP;
					_logger.LogInformation( "[Client] Heal!!! healerId: {healerId} -> TargetId: {TargetId}", packet.Healer.ObjectId, target.ObjectId );
					_logger.LogInformation( "HealAmount: {healAmount} | oldHP: {oldHP} -> CurrentHP: {CurrentHP} (ChangeValue: {Change}) ",
					packet.Healer.Damage, oldHP, target.CurrentHP, target.CurrentHP - oldHP );
				}
			}

			return ValueTask.CompletedTask; 
		}
		public override ValueTask On_S_LevelUp( NetworkSession session, S_LevelUp packet )
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogInformation( "[Client] LEVEL UP! Player {PlayerId} -> Level {NewLevel}",
				packet.PlayerId, packet.NewLevel );
			_logger.LogInformation( "  ├─ Max HP: {NewMaxHP}", packet.NewMaxHP );
			_logger.LogInformation( "  └─ Max MP: {NewMaxMP}", packet.NewMaxMP );

			if(packet.PlayerId == ctx.MyPlayer.PlayerId)
			{
				ctx.MyPlayer.Stats.Level = packet.NewLevel;
				ctx.MyPlayer.Stats.MaxHP = packet.NewMaxHP;
				ctx.MyPlayer.Stats.MaxMP = packet.NewMaxMP;
				ctx.MyPlayer.Stats.CurrentHP = packet.NewMaxHP;
				ctx.MyPlayer.Stats.CurrentMP = packet.NewMaxMP;
				ctx.MyPlayer.Stats.Experience = 0;
			}

			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_InventoryData( NetworkSession session, S_InventoryData packet )
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogInformation( "========================================" );
			_logger.LogInformation( "[Client] S_InventoryData - 인벤토리 조회" );
			_logger.LogInformation( "========================================" );
			_logger.LogInformation( "인벤토리 용량: {Count}/{MaxSlots}", packet.Items.Count, packet.MaxSlots );
			_logger.LogInformation( "골드: {Gold}", packet.Gold );

			if(0 < packet.Items.Count)
			{
				_logger.LogInformation( "보유 아이템:" );
				foreach(var item in packet.Items)
				{
					string itemInfo = $"  [슬롯 {item.Slot}] 아이템 ID: {item.ItemId} x{item.Quantity}";
					if(0 < item.EnhancementLevel)
					{
						itemInfo += $" +{item.EnhancementLevel}";
					}
					_logger.LogInformation( itemInfo );
				}
			}
			else
			{
				_logger.LogInformation( "보유 아이템: 없음" );
			}

			_logger.LogInformation( "========================================" );

			// 포션 슬롯 감지
			var healthPotion = packet.Items.FirstOrDefault(i => i.ItemId == Program.HealthPotionItemId);
			if(healthPotion != null)
			{
				ctx.HealthPotionSlot = healthPotion.Slot;
				_logger.LogInformation( "[Potion] HP 포션 감지: 슬롯 {Slot}, 수량 x{Quantity}",
					healthPotion.Slot, healthPotion.Quantity );
			}
			else
			{
				ctx.HealthPotionSlot = -1;
				_logger.LogDebug( "[Potion] HP 포션 없음" );
			}
			
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_UseItem( NetworkSession session, S_UseItem packet ) 
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogInformation( "========================================" );
			_logger.LogInformation( "[Client] S_UseItem - 아이템 사용" );
			_logger.LogInformation( "========================================" );

			if(packet.Success)
			{
				_logger.LogInformation( "[성공] 아이템 사용 완료" );
				_logger.LogInformation( "  슬롯: {Slot}", packet.Slot );
				_logger.LogInformation( "  남은 수량: x{RemainingQuantity}",
					packet.RemainingQuantity );

				// 서버 메시지 표시
				if(!string.IsNullOrEmpty( packet.Message ))
				{
					_logger.LogInformation( "  메시지: {Message}", packet.Message );
				}
			}
			else
			{
				_logger.LogWarning( "[실패] 아이템 사용 실패" );
				_logger.LogWarning( "  사유: {Reason}", packet.Message ?? "알 수 없음" );
			}

			_logger.LogInformation( "========================================" );
			return ValueTask.CompletedTask; 
		}
		public override ValueTask On_S_ItemEquipped( NetworkSession session, S_ItemEquipped packet )
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogInformation( "========================================" );
			_logger.LogInformation( "[Client] S_ItemEquipped - 아이템 장착" );
			_logger.LogInformation( "========================================" );

			if(packet.Success)
			{
				_logger.LogInformation( "[성공] 장착 완료" );
				_logger.LogInformation( "  인벤토리 슬롯: {InventorySlot} -> 장착 슬롯: {EquipSlot} ({EquipSlotName})",
					packet.InventorySlot, packet.EquipSlot, GetEquipSlotName( packet.EquipSlot ) );

				// 현재 장착 장비 전체 표시
				_logger.LogInformation( "현재 장착 장비:" );
				_logger.LogInformation( "  무기: {Weapon}", packet.UpdatedEquipment.WeaponItemId == 0 ? "없음" : $"아이템 {packet.UpdatedEquipment.WeaponItemId}" );
				_logger.LogInformation( "  갑옷: {Armor}", packet.UpdatedEquipment.ArmorItemId == 0 ? "없음" : $"아이템 {packet.UpdatedEquipment.ArmorItemId}" );
				_logger.LogInformation( "  헬멧: {Helmet}", packet.UpdatedEquipment.HelmetItemId == 0 ? "없음" : $"아이템 {packet.UpdatedEquipment.HelmetItemId}" );
				_logger.LogInformation( "  장갑: {Gloves}", packet.UpdatedEquipment.GlovesItemId == 0 ? "없음" : $"아이템 {packet.UpdatedEquipment.GlovesItemId}" );

				// 현재 스탯 전체 표시
				_logger.LogInformation( "현재 스탯:" );
				_logger.LogInformation( "  공격력: {Attack}", packet.UpdatedStats.Attack );
				_logger.LogInformation( "  방어력: {Defense}", packet.UpdatedStats.Defense );
				_logger.LogInformation( "  HP: {CurrentHP}/{MaxHP}", packet.UpdatedStats.CurrentHP, packet.UpdatedStats.MaxHP );
				_logger.LogInformation( "  MP: {CurrentMP}/{MaxMP}", packet.UpdatedStats.CurrentMP, packet.UpdatedStats.MaxMP );

				// 내 플레이어 정보 업데이트
				ctx.MyPlayer.Stats = packet.UpdatedStats.Clone();
			}
			else
			{
				_logger.LogWarning( "[실패] 장착 실패" );
				_logger.LogWarning( "  인벤토리 슬롯: {InventorySlot}, 장착 슬롯: {EquipSlot}",
					packet.InventorySlot, packet.EquipSlot );
			}

			_logger.LogInformation( "========================================" );
			return ValueTask.CompletedTask;
		}

		// 장착 슬롯 번호 → 이름 변환
		private string GetEquipSlotName( int slotNumber )
		{
			return slotNumber switch
			{
				0 => "무기",
				1 => "갑옷",
				2 => "헬멧",
				3 => "장갑",
				_ => "알 수 없음"
			};
		}
		public override ValueTask On_S_ItemUnequipped( NetworkSession session, S_ItemUnequipped packet ) 
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogInformation( "Received but not handled: S_ItemUnequipped" );

			return ValueTask.CompletedTask; 
		}
		public override ValueTask On_S_ItemAdded( NetworkSession session, S_ItemAdded packet )
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogInformation( "========================================" );
			_logger.LogInformation( "[Client] S_ItemAdded - 아이템 획득!" );
			_logger.LogInformation( "========================================" );
			_logger.LogInformation( "아이템 ID: {itemId}", packet.Item.ItemId );
			_logger.LogInformation( "수량: x{Count}", packet.Item.Quantity );
			_logger.LogInformation( "인벤토리 슬롯: {Slot}", packet.Item.Slot );
			_logger.LogInformation( "획득처: {Source}", packet.Source );
			_logger.LogInformation( "========================================" );

			return ValueTask.CompletedTask;
		}
		public override ValueTask On_S_InventoryUpdate( NetworkSession session, S_InventoryUpdate packet ) 
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogInformation( "Received but not handled: S_InventoryUpdate" ); 
			return ValueTask.CompletedTask; 
		}

		public override ValueTask On_S_MonsterDie( NetworkSession session, S_MonsterDie packet )
		{
			var ctx = ((ServerSession)session).Context;

			string objectName = ctx.NearbyObjects.TryGetValue(packet.MonsterId, out var monster)
				? monster.Name : $"Monster {packet.MonsterId}";

			_logger.LogInformation( "[Client] Monster Killed! {Name} (ID:{MonsterId})",
	objectName, packet.MonsterId );
			_logger.LogInformation( "  ├─ Killer: Player {KillerId}", packet.KillPlayerId );
			_logger.LogInformation( "  ├─ Exp Gained: +{Exp}", packet.ExpGained );
			_logger.LogInformation( "  ├─ Gold Gained: +{Gold}", packet.GoldGained );

			if(0 < packet.DroppedItems.Count)
			{
				_logger.LogInformation( "  └─ Items Dropped: {Count} items", packet.DroppedItems.Count );
				foreach(var item in packet.DroppedItems)
				{
					_logger.LogInformation( "     └─ Item {ItemId} x{Quantity}", item.ItemId, item.Quantity );
				}
			}

			// 몬스터 상태를 Dead로 변경 (제거하지 않음!)
			if(ctx.NearbyObjects.TryGetValue(packet.MonsterId, out var targetMonster))
			{
				targetMonster.State = State.Dead;	// 사망 상태로 변경
				//ctx.NearbyObjects[packet.MonsterId] = targetMonster;
				_logger.LogInformation( "[Client] Monster {MonsterId} state changed to DEAD (Waiting for despawn...)",
					packet.MonsterId );
			}

			// 타겟이 죽었으면 새 타겟 찾기
			if(ctx.TargetMonsterId == packet.MonsterId)
			{
				ctx.TargetMonsterId = FindNewTarget(ctx);
				if(0 < ctx.TargetMonsterId)
				{
					_logger.LogInformation( "New target: Monster {MonsterId}", ctx.TargetMonsterId );
				}
			}
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_MonsterUpdate( NetworkSession session, S_MonsterUpdate packet )
		{
			var ctx = ((ServerSession)session).Context;

			foreach(var monster in packet.Monsters)
			{
				// 기존 몬스터 정보 업데이트
				if(ctx.NearbyObjects.ContainsKey(monster.ObjectId))
				{
					var oldMonster = ctx.NearbyObjects[monster.ObjectId];

					// HP 변화 로그
					if(oldMonster.StatInfo.CurrentHP != monster.StatInfo.CurrentHP)
					{
						int hpChange = monster.StatInfo.CurrentHP - oldMonster.StatInfo.CurrentHP;
						string changeStr = 0 < hpChange ? $"+{hpChange}" : hpChange.ToString();

						_logger.LogInformation( "[Client] S_MonsterUpdate - Monster {MonsterId} HP: {OldHP} -> {NewHP} ({Change})",
							monster.ObjectId, oldMonster.StatInfo.CurrentHP, monster.StatInfo.CurrentHP, changeStr );
					}

					// 상태 변경 로그
					if (oldMonster.State != monster.State)
					{
						_logger.LogInformation( "[Client] S_MonsterUpdate - Monster {MonsterId} State: {OldState} -> {NewState}",
							monster.ObjectId, oldMonster.State, monster.State );
					}

					ctx.NearbyObjects[ monster.ObjectId ] = monster;
				}
			}

			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_PathInfo( NetworkSession session, S_PathInfo packet )
		{
			var ctx = ((ServerSession)session).Context;

			if(packet.Waypoints.Count == 0)
			{
				_logger.LogWarning( "[AutoMove] 경로 없음 - 이동 불가" );
				ctx.AutoMoveWaypoints.Clear();
				return ValueTask.CompletedTask;
			}

			string reachableStr = packet.Reachable ? "도달 가능" : "best-Effort";
			_logger.LogInformation( "[AutoMove] 경로 수신: {Count}개 웨이포인트({Reachable})", packet.Waypoints.Count, reachableStr );

			// 웨이 포인트 저장 (자동 이동 루프에서 사용)
			ctx.AutoMoveWaypoints = packet.Waypoints.ToList();
			ctx.AutoMoveIndex = 0;

			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_Pong( NetworkSession session, S_Pong packet )
		{
			var ctx = ((ServerSession)session).Context;

			_logger.LogDebug( "[Client] Received S_Pong. Timestamp: {Timestamp}", packet.Timestamp );
			return ValueTask.CompletedTask;
		}

		// 헬퍼 메서드 : 새 타겟 찾기
		private long FindNewTarget(ClientContext ctx)
		{
			if(ctx.NearbyObjects.Any(m => m.Value.Type == ObjectType.ObjectMonster) == false)
				return 0;

			// 살아 있는 몬스터 중 첫 번째 선택
			var aliveMonster = ctx.NearbyObjects.FirstOrDefault(m => m.Value.Type == ObjectType.ObjectMonster 
			&& m.Value.State != State.Dead);
			return aliveMonster.Value != null ? aliveMonster.Key : 0;
		}
	}
}