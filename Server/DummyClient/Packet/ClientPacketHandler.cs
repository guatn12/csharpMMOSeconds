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
			_logger.LogInformation( "[Client] S_EnterGame - PlayerId: {PlayerId}, " +
				"PlayerName: {PlayerName}, Level: {Level}, HP: {HP}/{MaxHP}",
				packet.Player.PlayerId, packet.Player.Name,
				packet.Player.Level, packet.Player.CurrentHP, packet.Player.MaxHP );

			Program.MyPlayer.PlayerId = packet.Player.PlayerId;
			Program.MyPlayer.PlayerName = packet.Player.Name;
			Program.MyPlayer.Level = packet.Player.Level;
			Program.MyPlayer.Stats.CurrentHP = packet.Player.CurrentHP;
			Program.MyPlayer.Stats.CurrentMP = packet.Player.CurrentMP;
			Program.MyPlayer.Stats.MaxHP = packet.Player.MaxHP;
			Program.MyPlayer.Stats.MaxMP = packet.Player.MaxMP;
			Program.MyPlayer.CurrentExp = packet.Player.Experience;

			// 위치 정보 초기화
			if(packet.Player.PosInfo != null)
			{
				Program.MyPlayer.Position.PosX = packet.Player.PosInfo.PosX;
				Program.MyPlayer.Position.PosY = packet.Player.PosInfo.PosY;
				Program.MyPlayer.Position.PosZ = packet.Player.PosInfo.PosZ;
				Program.MyPlayer.Position.RotationX = packet.Player.PosInfo.RotationX;
				Program.MyPlayer.Position.RotationY = packet.Player.PosInfo.RotationY;
				Program.MyPlayer.Position.RotationZ = packet.Player.PosInfo.RotationZ;
			}

			Program.MyPlayer.LogStatus( _logger );

			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_LeaveGame( NetworkSession session, S_LeaveGame packet )
		{
			_logger.LogInformation( "[Client] Received S_LeaveGame. PlayerId: {PlayerId}", packet.PlayerId );
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_Spawn( NetworkSession session, S_Spawn packet )
		{
			_logger.LogInformation( "[Client] S_Spawn - {PlayersCount} players spawned", packet.Players.Count );
			foreach(var player in packet.Players)
			{
				_logger.LogDebug( "  Player {PlayerId} at ({PosX:F2}, {PosY:F2}, {PosZ:F2})",
					player.PlayerId, player.PosInfo.PosX, player.PosInfo.PosY, player.PosInfo.PosZ );
			}
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_Despawn( NetworkSession session, S_Despawn packet )
		{
			_logger.LogInformation( "[Client] Received S_Despawn. ObjectIds: {ObjectIds}", string.Join( ", ", packet.ObjectIds ) );
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_Move( NetworkSession session, S_Move packet )
		{
			_logger.LogInformation( "[Client] S_Move - PlayerId: {PlayerId}, " +
				"3D Position: ({PosX:F2}, {PosY:F2}, {PosZ:F2}), " +
				"Rotation: ({RotX:F1}, {RotY:F1}, {RotZ:F1}), " +
				"Timestamp: {Timestamp}",
				packet.PlayerId,
				packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ,
				packet.PosInfo.RotationX, packet.PosInfo.RotationY, packet.PosInfo.RotationZ,
				packet.PosInfo.Timestamp );
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_Chat( NetworkSession session, S_Chat packet )
		{
			_logger.LogInformation( "[Client] Received S_Chat. PlayerId: {PlayerId}, Message: {Message}", packet.PlayerId, packet.Message );
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_PlayerUpdate( NetworkSession session, S_PlayerUpdate packet )
		{
			// 내 플레이어 정보만 업데이트
			if(packet.Player.PlayerId == Program.MyPlayer.PlayerId)
			{
				int oldHP = Program.MyPlayer.Stats.CurrentHP;
				int oldMP = Program.MyPlayer.Stats.CurrentMP;

				Program.MyPlayer.Stats.CurrentMP = packet.Player.CurrentMP;
				Program.MyPlayer.Stats.CurrentHP = packet.Player.CurrentHP;

				// HP 변화 로그
				if(oldHP != packet.Player.CurrentHP)
				{
					int hpChange = packet.Player.CurrentHP - oldHP;
					string changeStr = 0 < hpChange ? $"+{hpChange}" : hpChange.ToString();

					_logger.LogInformation( "HP : {OldHP} -> {NewHP} ({Change}) [{Percent:F1}%]",
						oldHP, packet.Player.CurrentHP, changeStr, Program.MyPlayer.HPPercent );

					// HP 위험 경고 (30% 이하)
					if(Program.MyPlayer.HPPercent < 30f && 30f <= ((float)oldHP / packet.Player.MaxHP * 100))
					{
						_logger.LogWarning( "HP 위험! 포션 사용 권장" );
					}
				}

				// MP 변화 로그
				if(oldMP != packet.Player.CurrentMP)
				{
					int mpChange = packet.Player.CurrentMP - oldMP;
					string changeStr = 0 < mpChange ? $"+{mpChange}" : mpChange.ToString();

					_logger.LogDebug( "MP: {OldMP} -> {NewMP} ({Change})",
						oldMP, packet.Player.CurrentMP, changeStr );
				}
			}

			return ValueTask.CompletedTask;
		}
		public override ValueTask On_S_PlayerStat( NetworkSession session, S_PlayerStat packet )
		{
			if(packet.Player.PlayerId == Program.MyPlayer.PlayerId)
			{
				Program.MyPlayer.Level = packet.Player.Level;
				Program.MyPlayer.Stats.CurrentHP = packet.Player.CurrentHP;
				Program.MyPlayer.Stats.MaxHP = packet.Player.MaxHP;
				Program.MyPlayer.Stats.CurrentMP = packet.Player.CurrentMP;
				Program.MyPlayer.Stats.MaxMP = packet.Player.MaxMP;
				Program.MyPlayer.CurrentExp = packet.Player.Experience;

				// 전체 출력
				Program.MyPlayer.LogStatus( _logger );
			}

			return ValueTask.CompletedTask;
		}
		public override ValueTask On_S_Damage( NetworkSession session, S_Damage packet )
		{
			// 공격자와 피해자 정보 파악
			string attackerName = packet.AttackerId < 1000
				? $"Player {packet.AttackerId}"
				: (Program.NearbyMonsters.TryGetValue(packet.AttackerId, out var attacker)
					? attacker.Name
					: $"Monster {packet.AttackerId}");

			// 몬스터의 경우 (임시 구분)
			if(1000 <= packet.TargetId)
			{
				if(!Program.NearbyMonsters.TryGetValue( packet.TargetId, out var target ))
				{
					_logger.LogWarning( "[Client] Target Monster {TargetId} Not Found From NearByMonsters", packet.TargetId );
					return ValueTask.CompletedTask;
				}

				target.CurrentHP = packet.CurrentHP;

				_logger.LogInformation( "[Client] Damage: {Attacker} -> {Target} | Damage: {Damage} | Remaining HP: {CurrentHP}",
					attackerName, target.Name, packet.Damage, packet.CurrentHP );
			}
			// 플레이인 경우(임시 구분)
			else
			{
				// 타겟이 나인 경우
				if(packet.TargetId == Program.MyPlayer.PlayerId)
				{
					Program.MyPlayer.Stats.CurrentHP = packet.CurrentHP;

					_logger.LogWarning( "[Client] 피격! {Attacker} -> 나 | Damage: {Damage} | Remaining HP:{ CurrentHP}/{ MaxHP} ({ Percent: F1}%)",
						attackerName, packet.Damage, packet.CurrentHP, Program.MyPlayer.Stats.MaxHP, Program.MyPlayer.HPPercent);

					// HP 위험 경고
					if(Program.MyPlayer.HPPercent < 30f)
					{
						_logger.LogError( "HP 위험! 포션 사용 또는 도망 필요!" );
					}
				}
			}

			return ValueTask.CompletedTask;
		}
		public override ValueTask On_S_Heal( NetworkSession session, S_Heal packet ) 
		{ 
			int oldHP = Program.MyPlayer.Stats.CurrentHP;
			if(packet.HealAmount <= 0)
			{
				_logger.LogWarning( "[Client] S_Heal - HealAmount({HealAmount}) is Zero or Negative!!!", packet.HealAmount);
				return ValueTask.CompletedTask;
			}

			// 힐 타겟이 나인 경우 HP 업데이트
			if(packet.TargetId == Program.MyPlayer.PlayerId)
			{
				Program.MyPlayer.Stats.CurrentHP = packet.CurrentHP;
			}

			_logger.LogInformation( "[Client] Heal!!! healerId: {healerId} -> TargetId: {TargetId}", packet.HealerId, packet.TargetId );
			_logger.LogInformation( "HealAmount: {healAmount} | oldHP: {oldHP} -> CurrentHP: {CurrentHP} (ChangeValue: {Change}) ",
				packet.HealAmount, oldHP, packet.CurrentHP, packet.CurrentHP - oldHP );


			return ValueTask.CompletedTask; 
		}
		public override ValueTask On_S_LevelUp( NetworkSession session, S_LevelUp packet )
		{
			_logger.LogInformation( "[Client] LEVEL UP! Player {PlayerId} -> Level {NewLevel}",
				packet.PlayerId, packet.NewLevel );
			_logger.LogInformation( "  ├─ Max HP: {NewMaxHP}", packet.NewMaxHP );
			_logger.LogInformation( "  └─ Max MP: {NewMaxMP}", packet.NewMaxMP );

			if(packet.PlayerId == Program.MyPlayer.PlayerId)
			{
				Program.MyPlayer.Level = packet.NewLevel;
				Program.MyPlayer.Stats.MaxHP = packet.NewMaxHP;
				Program.MyPlayer.Stats.MaxMP = packet.NewMaxMP;
				Program.MyPlayer.Stats.CurrentHP = packet.NewMaxHP;
				Program.MyPlayer.Stats.CurrentMP = packet.NewMaxMP;
				Program.MyPlayer.CurrentExp = 0;
			}

			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_InventoryData( NetworkSession session, S_InventoryData packet )
		{
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
				Program.HealthPotionSlot = healthPotion.Slot;
				_logger.LogInformation( "[Potion] HP 포션 감지: 슬롯 {Slot}, 수량 x{Quantity}",
					healthPotion.Slot, healthPotion.Quantity );
			}
			else
			{
				Program.HealthPotionSlot = -1;
				_logger.LogDebug( "[Potion] HP 포션 없음" );
			}
			
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_UseItem( NetworkSession session, S_UseItem packet ) 
		{
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
				Program.MyPlayer.Stats.Attack = packet.UpdatedStats.Attack;
				Program.MyPlayer.Stats.Defense = packet.UpdatedStats.Defense;
				Program.MyPlayer.Stats.MaxHP = packet.UpdatedStats.MaxHP;
				Program.MyPlayer.Stats.MaxMP = packet.UpdatedStats.MaxMP;
				Program.MyPlayer.Stats.CurrentHP = packet.UpdatedStats.CurrentHP;
				Program.MyPlayer.Stats.CurrentMP = packet.UpdatedStats.CurrentMP;
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
		public override ValueTask On_S_ItemUnequipped( NetworkSession session, S_ItemUnequipped packet ) { Console.WriteLine( "Received but not handled: S_ItemUnequipped" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_ItemAdded( NetworkSession session, S_ItemAdded packet )
		{
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
		public override ValueTask On_S_InventoryUpdate( NetworkSession session, S_InventoryUpdate packet ) { Console.WriteLine( "Received but not handled: S_InventoryUpdate" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_MonsterSpawn( NetworkSession session, S_MonsterSpawn packet )
		{
			_logger.LogInformation( "[Client] S_MonsterSpawn - {Count} monsters spawned", packet.Monsters.Count );

			foreach(var monster in packet.Monsters)
			{
				_logger.LogInformation( "Monster {MonsterId}: {Name} (Lv.{Level})" + "" +
					"HP:{CurrentHP}/{MaxHP} State:{State} at ({PosX:F1},{PosY:F1},{PosZ:F1})",
					monster.MonsterId, monster.Name, monster.Level, monster.CurrentHP, monster.MaxHP,
					monster.State, monster.PosInfo.PosX, monster.PosInfo.PosY, monster.PosInfo.PosZ );

				// 몬스터 정보를 Program의 정적 딕셔너리에 저장
				Program.NearbyMonsters[ monster.MonsterId ] = monster;
			}

			// 자동 타겟 설정 (첫 번째 몬스터)(
			if (Program.TargetMonsterId == 0 && 0 < packet.Monsters.Count)
			{
				Program.TargetMonsterId = packet.Monsters[0].MonsterId;
				_logger.LogInformation( "Auto-target set to Monster {MonsterId}",
					Program.TargetMonsterId );
			}

			return ValueTask.CompletedTask;
		}
		public override ValueTask On_S_MonsterDespawn( NetworkSession session, S_MonsterDespawn packet )
		{
			_logger.LogInformation( "[Client] S_MonsterDespawn - {Count} monsters removed",
				packet.MonsterIds.Count );

			foreach(var monsterId in packet.MonsterIds)
			{
				if(Program.NearbyMonsters.TryGetValue( monsterId, out MonsterInfo monster ))
				{
					_logger.LogInformation( "Removed Monster {MonsterId}: {Name}", monsterId, monster.Name );
					Program.NearbyMonsters.Remove( monsterId );
				}

				// 타겟이 제거되었으면 초기화
				if(Program.TargetMonsterId == monsterId)
				{
					Program.TargetMonsterId = 0;
					_logger.LogWarning( "Current target removed, searching new target..." );
				}
			}

			return ValueTask.CompletedTask;
		}
		public override ValueTask On_S_MonsterMove( NetworkSession session, S_MonsterMove packet ) { Console.WriteLine( "Received but not handled: S_MonsterMove" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_MonsterAttack( NetworkSession session, S_MonsterAttack packet )
		{
			string monsterName = Program.NearbyMonsters.TryGetValue(packet.MonsterId, out var monster)
				? monster.Name
				: $"Monster {packet.MonsterId}";

			_logger.LogWarning( "[Client] Monster Attack! {Name} attacked Player {PlayerId} for {Damage} damage",
				monsterName, packet.TargetPlayerId, packet.Damage );

			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_MonsterDie( NetworkSession session, S_MonsterDie packet )
		{
			string monsterName = Program.NearbyMonsters.TryGetValue(packet.MonsterId, out var monster)
				? monster.Name : $"Monster {packet.MonsterId}";

			_logger.LogInformation( "[Client] Monster Killed! {Name} (ID:{MonsterId})",
	monsterName, packet.MonsterId );
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
			if(Program.NearbyMonsters.TryGetValue(packet.MonsterId, out var targetMonster))
			{
				targetMonster.State = MonsterState.MonsterDie;	// 사망 상태로 변경
				Program.NearbyMonsters[packet.MonsterId] = targetMonster;
				_logger.LogInformation( "[Client] Monster {MonsterId} state changed to DEAD (Waiting for despawn...)",
					packet.MonsterId );
			}

			// 타겟이 죽었으면 새 타겟 찾기
			if(Program.TargetMonsterId == packet.MonsterId)
			{
				Program.TargetMonsterId = FindNewTarget();
				if(0 < Program.TargetMonsterId)
				{
					_logger.LogInformation( "New target: Monster {MonsterId}", Program.TargetMonsterId );
				}
			}
			return ValueTask.CompletedTask;
		}

		public override ValueTask On_S_MonsterUpdate( NetworkSession session, S_MonsterUpdate packet )
		{
			foreach(var monster in packet.Monsters)
			{
				// 기존 몬스터 정보 업데이트
				if(Program.NearbyMonsters.ContainsKey(monster.MonsterId))
				{
					var oldMonster = Program.NearbyMonsters[monster.MonsterId];

					// HP 변화 로그
					if(oldMonster.CurrentHP != monster.CurrentHP)
					{
						int hpChange = monster.CurrentHP - oldMonster.CurrentHP;
						string changeStr = 0 < hpChange ? $"+{hpChange}" : hpChange.ToString();

						_logger.LogDebug( "[Client] Monster {MonsterId} HP: {OldHP} -> {NewHP} ({Change})",
							monster.MonsterId, oldMonster.CurrentHP, monster.CurrentHP, changeStr );
					}

					// 상태 변경 로그
					if (oldMonster.State != monster.State)
					{
						_logger.LogDebug( "[Client] Monster {MonsterId} State: {OldState} -> {NewState}",
							monster.MonsterId, oldMonster.State, monster.State );
					}

					Program.NearbyMonsters[ monster.MonsterId ] = monster;
				}
			}

			return ValueTask.CompletedTask;
		}

		// 헬퍼 메서드 : 새 타겟 찾기
		private long FindNewTarget()
		{
			if(Program.NearbyMonsters.Count == 0)
				return 0;

			// 살아 있는 몬스터 중 첫 번째 선택
			var aliveMonster = Program.NearbyMonsters.FirstOrDefault(m => m.Value.State != MonsterState.MonsterDie);
			return aliveMonster.Value != null ? aliveMonster.Key : 0;
		}
	}
}