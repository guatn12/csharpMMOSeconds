using DummyClient.Packet;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Protocol; // Added for packet types
using Microsoft.Extensions.Logging; // Added for ILogger

namespace DummyClient.Packet
{
    public class ClientPacketHandler : BaseClientPacketHandler
    {
        private readonly ILogger<ClientPacketHandler> _logger;

        public ClientPacketHandler(ILogger<ClientPacketHandler> logger)
        {
            _logger = logger;
        }

        public override ValueTask On_S_EnterGame(Session session, S_EnterGame packet)
        {
			_logger.LogInformation( "[Client] S_EnterGame - PlayerId: {PlayerId}, " +
				"PlayerName: {PlayerName}, Level: {Level}, HP: {HP}/{MaxHP}",
				packet.Player.PlayerId, packet.Player.Name,
				packet.Player.Level, packet.Player.CurrentHP, packet.Player.MaxHP );

			Program.MyPlayer.PlayerId = packet.Player.PlayerId;
			Program.MyPlayer.PlayerName = packet.Player.Name;
			Program.MyPlayer.Level = packet.Player.Level;
			Program.MyPlayer.CurrentHP = packet.Player.CurrentHP;
			Program.MyPlayer.CurrentMP = packet.Player.CurrentMP;
			Program.MyPlayer.MaxHP = packet.Player.MaxHP;
			Program.MyPlayer.MaxMP = packet.Player.MaxMP;
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

        public override ValueTask On_S_LeaveGame(Session session, S_LeaveGame packet)
        {
            _logger.LogInformation("[Client] Received S_LeaveGame. PlayerId: {PlayerId}", packet.PlayerId);
            return ValueTask.CompletedTask;
        }

        public override ValueTask On_S_Spawn(Session session, S_Spawn packet)
        {
			_logger.LogInformation( "[Client] S_Spawn - {PlayersCount} players spawned", packet.Players.Count );
			foreach(var player in packet.Players)
			{
				_logger.LogDebug( "  Player {PlayerId} at ({PosX:F2}, {PosY:F2}, {PosZ:F2})",
					player.PlayerId, player.PosInfo.PosX, player.PosInfo.PosY, player.PosInfo.PosZ );
			}
			return ValueTask.CompletedTask;
		}

        public override ValueTask On_S_Despawn(Session session, S_Despawn packet)
        {
            _logger.LogInformation("[Client] Received S_Despawn. ObjectIds: {ObjectIds}", string.Join(", ", packet.ObjectIds));
            return ValueTask.CompletedTask;
        }

        public override ValueTask On_S_Move(Session session, S_Move packet)
        {
			_logger.LogInformation( "[Client] S_Move - PlayerId: {PlayerId}, " +
				"3D Position: ({PosX:F2}, {PosY:F2}, {PosZ:F2}), " +
				"Rotation: ({RotX:F1}°, {RotY:F1}°, {RotZ:F1}°), " +
				"Timestamp: {Timestamp}",
				packet.PlayerId,
				packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ,
				packet.PosInfo.RotationX, packet.PosInfo.RotationY, packet.PosInfo.RotationZ,
				packet.PosInfo.Timestamp );
			return ValueTask.CompletedTask;
		}

        public override ValueTask On_S_Chat(Session session, S_Chat packet)
        {
            _logger.LogInformation("[Client] Received S_Chat. PlayerId: {PlayerId}, Message: {Message}", packet.PlayerId, packet.Message);
            return ValueTask.CompletedTask;
        }

		public override ValueTask On_S_PlayerUpdate( Session session, S_PlayerUpdate packet ) 
		{ 
			// 내 플레이어 정보만 업데이트
			if(packet.Player.PlayerId == Program.MyPlayer.PlayerId)
			{
				int oldHP = Program.MyPlayer.CurrentHP;
				int oldMP = Program.MyPlayer.CurrentMP;

				Program.MyPlayer.CurrentMP = packet.Player.CurrentMP;
				Program.MyPlayer.CurrentHP = packet.Player.CurrentHP;

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

					_logger.LogDebug( "MP: {OldMP} → {NewMP} ({Change})",
						oldMP, packet.Player.CurrentMP, changeStr );
				}
			}

			return ValueTask.CompletedTask; 
		}
		public override ValueTask On_S_PlayerStat( Session session, S_PlayerStat packet ) 
		{ 
			if(packet.Player.PlayerId == Program.MyPlayer.PlayerId)
			{
				Program.MyPlayer.Level = packet.Player.Level;
				Program.MyPlayer.CurrentHP = packet.Player.CurrentHP;
				Program.MyPlayer.MaxHP = packet.Player.MaxHP;
				Program.MyPlayer.CurrentMP = packet.Player.CurrentMP;
				Program.MyPlayer.MaxMP = packet.Player.MaxMP;
				Program.MyPlayer.CurrentExp = packet.Player.Experience;

				// 전체 출력
				Program.MyPlayer.LogStatus( _logger );
			}

			return ValueTask.CompletedTask; 
		}
		public override ValueTask On_S_Damage( Session session, S_Damage packet ) 
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

				_logger.LogInformation( "[Client] Damage: {Attacker} → {Target} | Damage: {Damage} | Remaining HP: {CurrentHP}",
					attackerName, target.Name, packet.Damage, packet.CurrentHP );
			}
			// 플레이인 경우(임시 구분)
			else
			{
				// 타겟이 나인 경우
				if(packet.TargetId == Program.MyPlayer.PlayerId)
				{
					Program.MyPlayer.CurrentHP = packet.CurrentHP;

					_logger.LogWarning( "[Client] 피격! {Attacker} → 나 | Damage: {Damage} | Remaining HP:{ CurrentHP}/{ MaxHP} ({ Percent: F1}%)",
						attackerName, packet.Damage, packet.CurrentHP, Program.MyPlayer.MaxHP, Program.MyPlayer.HPPercent);

					// HP 위험 경고
					if(Program.MyPlayer.HPPercent < 30f)
					{
						_logger.LogError( "HP 위험! 포션 사용 또는 도망 필요!" );
					}
				}
			}

			return ValueTask.CompletedTask; 
		}
		public override ValueTask On_S_Heal( Session session, S_Heal packet ) { Console.WriteLine( "Received but not handled: S_Heal" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_LevelUp( Session session, S_LevelUp packet ) 
		{
			_logger.LogInformation( "[Client] LEVEL UP! Player {PlayerId} → Level {NewLevel}",
				packet.PlayerId, packet.NewLevel );
			_logger.LogInformation( "  ├─ Max HP: {NewMaxHP}", packet.NewMaxHP );
			_logger.LogInformation( "  └─ Max MP: {NewMaxMP}", packet.NewMaxMP );
			
			if(packet.PlayerId == Program.MyPlayer.PlayerId)
			{
				Program.MyPlayer.Level = packet.NewLevel;
				Program.MyPlayer.MaxHP = packet.NewMaxHP;
				Program.MyPlayer.MaxMP = packet.NewMaxMP;
				Program.MyPlayer.CurrentHP = packet.NewMaxHP;
				Program.MyPlayer.CurrentMP = packet.NewMaxMP;
				Program.MyPlayer.CurrentExp = 0;
			}
			
			return ValueTask.CompletedTask; 
		}
		public override ValueTask On_S_InventoryData( Session session, S_InventoryData packet ) { Console.WriteLine( "Received but not handled: S_InventoryData" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_UseItem( Session session, S_UseItem packet ) { Console.WriteLine( "Received but not handled: S_UseItem" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_ItemEquipped( Session session, S_ItemEquipped packet ) { Console.WriteLine( "Received but not handled: S_ItemEquipped" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_ItemUnequipped( Session session, S_ItemUnequipped packet ) { Console.WriteLine( "Received but not handled: S_ItemUnequipped" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_ItemAdded( Session session, S_ItemAdded packet ) { Console.WriteLine( "Received but not handled: S_ItemAdded" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_InventoryUpdate( Session session, S_InventoryUpdate packet ) { Console.WriteLine( "Received but not handled: S_InventoryUpdate" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_MonsterSpawn( Session session, S_MonsterSpawn packet ) 
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
		public override ValueTask On_S_MonsterDespawn( Session session, S_MonsterDespawn packet ) 
		{
			_logger.LogInformation( "[Client] S_MonsterDespawn - {Count} monsters removed",
				packet.MonsterIds.Count );

			foreach(var monsterId in packet.MonsterIds)
			{
				if(Program.NearbyMonsters.TryGetValue(monsterId, out MonsterInfo monster))
				{
					_logger.LogInformation( "Removed Monster {MonsterId}: {Name}", monsterId, monster.Name );
					Program.NearbyMonsters.Remove(monsterId);
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
		public override ValueTask On_S_MonsterMove( Session session, S_MonsterMove packet ) { Console.WriteLine( "Received but not handled: S_MonsterMove" ); return ValueTask.CompletedTask; }
		public override ValueTask On_S_MonsterAttack( Session session, S_MonsterAttack packet ) 
		{
			string monsterName = Program.NearbyMonsters.TryGetValue(packet.MonsterId, out var monster)
				? monster.Name
				: $"Monster {packet.MonsterId}";

			_logger.LogWarning( "[Client] Monster Attack! {Name} attacked Player {PlayerId} for {Damage} damage",
				monsterName, packet.TargetPlayerId, packet.Damage );

			return ValueTask.CompletedTask; 
		}
		
		public override ValueTask On_S_MonsterDie( Session session, S_MonsterDie packet ) 
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
				if(0 < Program.TargetMonsterId )
				{
					_logger.LogInformation( "New target: Monster {MonsterId}", Program.TargetMonsterId );
				}
			}
			return ValueTask.CompletedTask; 
		}
		
		public override ValueTask On_S_MonsterUpdate( Session session, S_MonsterUpdate packet ) 
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

						_logger.LogDebug( "[Client] Monster {MonsterId} HP: {OldHP} → {NewHP} ({Change})",
							monster.MonsterId, oldMonster.CurrentHP, monster.CurrentHP, changeStr );
					}

					// 상태 변경 로그
					if (oldMonster.State != monster.State)
					{
						_logger.LogDebug( "[Client] Monster {MonsterId} State: {OldState} → {NewState}",
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