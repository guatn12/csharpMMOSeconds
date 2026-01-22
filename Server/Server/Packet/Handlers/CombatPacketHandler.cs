using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Game.Monsters;
using Server.Room;
using Server.Services.Combat;
using Server.Services.DTOs;
using Server.Services.Reward;
using Server.Utils;
using System.Threading.Tasks;

namespace Server.Packet.Handlers
{
	public partial class CombatPacketHandler
	{
		private readonly ILogger<CombatPacketHandler> _logger;
		private readonly BaseRoom _room;
		private readonly ICombatService _combatService;
		private readonly IRewardService _rewardService;

		public CombatPacketHandler(ILogger<CombatPacketHandler> logger, BaseRoom room, 
			ICombatService combatService, IRewardService rewardService )
		{
			_logger = logger;
			_room = room;
			_combatService = combatService;
			_rewardService = rewardService;
			InitializeHandlers();
		}

		private async Task HandleC_AttackMonsterAsync( IClientSession session, C_AttackMonster packet)
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "AttackMonster validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 2. Monster 검증
			Monster monster = _room.MonsterManager?.GetMonster(packet.MonsterId);
			var monsterValidation = PacketValidators.ValidateMonster(monster);
			if(!monsterValidation.IsValid)
			{
				_logger.LogWarning("Monster validation failed: {Error}", monsterValidation.ErrorMessage );
				return;
			}

			// 3. 전투 처리
			CombatResults result = await _combatService.ProcessPlayerAttackMonsterAsync(session.Player, monster);
			if(result == null)
			{
				_logger.LogWarning( "Combat service returned null" );
				return;
			}

			_logger.LogInformation("Player {PlayerId} attacked Monster {MonsterId} for {Damage} damage",
				session.PlayerId, packet.MonsterId, result.Damage);

			// 4. 응답
			_room.Broadcast( new S_Damage
			{
				AttackerId = result.AttackerId,
				TargetId = result.TargetId,
				Damage = result.Damage,
				CurrentHP = result.TargetCurrentHP
			} );

			_room.Broadcast( new S_MonsterUpdate
			{
				Monsters = { monster.Info }
			} );

			// 5. 몬스터 사망 처리
			if(result.TargetDied)
			{
				await HandleMonsterDeathAsync( monster, session.PlayerId );
			}
		}

		private async Task HandleMonsterDeathAsync( Monster monster, long killerPlayerId )
		{
			IClientSession killerSession = _room.FindPlayerToPlayerId(killerPlayerId);
			if(killerSession == null)
			{
				_logger.LogWarning( "Killer player {PlayerId} not found", killerPlayerId );
				return;
			}

			// 1. 보상 계산
			RewardInfo reward = await _rewardService.CalculateMonsterRewardAsync(killerSession.Player, monster);

			// 2. 보상 지급 (이벤트 발생: OnLevelUp → S_LevelUp, OnItemAdded → S_ItemAdded)
			await _rewardService.GiveRewardAsync( killerSession.Player, reward );

			// 3. S_MonsterDie 브로드캐스트 (보상 정보 포함)
			var diePacket = new S_MonsterDie
			{
				MonsterId = monster.MonsterId,
				KillPlayerId = killerPlayerId,
				ExpGained = reward.Experience,
				GoldGained = reward.Gold
			};
			diePacket.DroppedItems.AddRange( reward.DroppedItem );
			_room.Broadcast( diePacket );

			// 4. 몬스터 제거
			_room.MonsterManager.DespawnMonster( monster.MonsterId );

			_logger.LogInformation( "Monster {MonsterId} defeated by Player {PlayerId}, Exp={Exp}, Gold={Gold}, LevelUp={LevelUp}",
				monster.MonsterId, killerPlayerId, reward.Experience, reward.Gold, reward.LeveledUp );
		}
	}
}
