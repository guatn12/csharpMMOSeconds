using Microsoft.Extensions.Logging;
using Protocol;
using Server.Data.Models;
using Server.Game;
using Server.Game.Monsters;
using Server.Services.DTOs;
using Server.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.Combat
{
	/// <summary>
	/// 전투 로직을 담당하는 Service 구현체
	/// </summary>
	public class CombatService : ICombatService
	{
		private readonly ILogger<CombatService> _logger;

		public CombatService(ILogger<CombatService> logger)
		{
			_logger = logger;
		}

		public (int damage, bool isCritical) CalculateDamage( int attackPower, int defense, int criticalRate = 10 )
		{
			// 기본 데미지
			int baseDamage = Math.Max(10, attackPower - defense);

			// 크리 판정 - thread safe 하게 동작 할 수 있도록 radon.shared(내부 적으로 TL을 사용하여 safe) 사용.
			bool isCritical = Random.Shared.Next(100) < criticalRate;

			// 크리 데미지
			int finalDamage = isCritical ? (int)(baseDamage*1.5f) : baseDamage;

			return (finalDamage, isCritical);
		}

		public bool IsInAttackRange( PosInfo attackerPos, PosInfo targetPos, float range = 5 )
		{
			if(attackerPos == null || targetPos == null)
			{
				_logger.LogWarning( "Invalid position: attackerPos or targetPos is null" );
				return false;
			}

			float distance = Position3DValidator.CalculateDistance3D(attackerPos, targetPos);

			return distance <= range;
		}

		public async Task<CombatResults> ProcessPlayerAttackMonsterAsync( Player player, Monster monster, SkillData skillData )
		{
			// 기본 검증
			if(player == null || monster == null)
			{
				_logger.LogWarning( "Invalid attack: player or monster is null" );
				return null;
			}

			if(!player.IsAlive)
			{
				_logger.LogWarning( "Dead player {PlayerId} tried to attack monster {MonsterId}",
					player.ObjectId, monster.ObjectId );
				return null;
			}

			if(!monster.IsAlive)
			{
				_logger.LogWarning( "Player {PlayerId} tried to attack dead monster {MonsterId}",
					player.ObjectId, monster.ObjectId );
				return null;
			}

			// 거리 검증
			if(!IsInAttackRange(player.PosInfo, monster.PosInfo))
			{
				_logger.LogWarning( "Player {PlayerId} out of attack range for monster {MonsterId}",
					player.ObjectId, monster.ObjectId );
				return null;
			}

			// 데미지 계산
			int attackPower = player.GetTotalAttack() + ( player.GetTotalAttack() * skillData.Damage / 100); // 스킬 데미지 비율 적용
			int defense = monster.StaticData.Defense;
			var (damage, isCritical) = CalculateDamage( attackPower, defense );

			// 데미지 적용
			bool damaged = monster.TakeDamage(damage, player.ObjectId);
			if(!damaged)
			{
				_logger.LogWarning( "Failed to apply damage to monster {MonsterId}", monster.ObjectId );
				return null;
			}

			// 공격 쿨다운 시작
			player.StartAttackCooldown();
			_logger.LogInformation(
				 "Player {PlayerId} attacked Monster {MonsterId} for {Damage} damage (Critical: {IsCritical}) - Remaining HP: {CurrentHP}",
				 player.ObjectId, monster.ObjectId, damage, isCritical, monster.CurrentHP );

			// 결과 반환
			return new CombatResults
			{
				AttackerInfo = player,
				TargetInfo = monster,
				Damage = damage,
				IsCritical = isCritical,
				TargetCurrentHP = monster.CurrentHP,
				TargetDied = !monster.IsAlive
			};
		}
	}
}
