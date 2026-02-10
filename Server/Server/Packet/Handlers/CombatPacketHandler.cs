using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Data;
using Server.Extensions;
using Server.Game.Monsters;
using Server.Room;
using Server.Services.Combat;
using Server.Services.DTOs;
using Server.Services.Reward;
using Server.Utils;
using System;
using System.Threading.Tasks;

namespace Server.Packet.Handlers
{
	public partial class CombatPacketHandler
	{
		private readonly ILogger<CombatPacketHandler> _logger;
		private readonly BaseRoom _room;
		private readonly ICombatService _combatService;
		private readonly IRewardService _rewardService;
		private readonly DataManager _dataManager;

		public CombatPacketHandler(ILogger<CombatPacketHandler> logger, BaseRoom room, 
			ICombatService combatService, IRewardService rewardService, DataManager dataManager )
		{
			_logger = logger;
			_room = room;
			_combatService = combatService;
			_rewardService = rewardService;
			_dataManager = dataManager;
			InitializeHandlers();
		}

		private async Task HandleC_UseSkillAsync(IClientSession session, C_UseSkill packet )
		{
			// 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "UseSkill validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			if(!session.Player.IsAlive)
			{
				_logger.LogWarning( "Player {PlayerId} is dead and cannot use skills", session.PlayerId );
				return;
			}

			Monster monster = _room.MonsterManager?.GetMonster(packet.TargetId);
			var monsterValidation = PacketValidators.ValidateMonster(monster);
			if(!monsterValidation.IsValid)
			{
				_logger.LogWarning( "Monster validation failed: {Error}", monsterValidation.ErrorMessage );
				return;
			}

			if(!await _room.ValidatePlayerUseSkillAsync( session, packet ))
			{
				_logger.LogWarning( "Skill validation failed for Player {PlayerId}, Skill {SkillId}",
					session.PlayerId, packet.SkillId );
				return;
			}

			// 4. 스킬 사용
			bool skillUsed = session.Player.UseSkill( packet.SkillId );
			if(!skillUsed)
			{
				_logger.LogWarning( "Player {PlayerId} failed to use skill {SkillId}",
					session.PlayerId, packet.SkillId );
				return;
			}

			// 룸별 스킬 효과 처리
			await _room.OnPlayerUseSkillAsync( session, packet );

			CombatResults result = await _combatService.ProcessPlayerAttackMonsterAsync(session.Player, monster);
			if(result == null)
			{
				_logger.LogWarning( "Combat service returned null" );
				return;
			}

			_logger.LogInformation( "Player {PlayerId} attacked Monster {TargetId} for {Damage} damage",
				session.PlayerId, packet.TargetId, result.Damage );

			_room.BroadcastInRange( new S_Damage
			{
				Attacker = result.AttackerInfo.ToObjectDamageInfo(result.Damage, result.IsCritical),
				Targets = { result.TargetInfo.ToObjectDamageInfo( result.Damage, result.IsCritical ) },
			}, session.Player.Position );

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
			_room.BroadcastInRange( diePacket, monster.Position );

			// 4. 몬스터 제거
			_room.MonsterManager.DespawnMonster( monster.MonsterId, TimeSpan.FromSeconds(_dataManager.GameConfig.MonsterDespawnDelaySeconds) );

			_logger.LogInformation( "Monster {MonsterId} defeated by Player {PlayerId}, Exp={Exp}, Gold={Gold}, LevelUp={LevelUp}",
				monster.MonsterId, killerPlayerId, reward.Experience, reward.Gold, reward.LeveledUp );
		}
	}
}
