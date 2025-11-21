using Microsoft.Extensions.Logging;
using Protocol;
using Server.Game;
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

		public async Task<CombatResults?> ProcessPlayerAttackMonsterAsync( Player player, Monster monster )
		{
			// 기본 검증
			if(player == null || monster == null)
			{
				_logger.LogWarning( "Invalid attack: player or monster is null" );
				return null;
			}

			if(!monster.IsAlive)
			{
				_logger.LogWarning( "Player {PlayerId} tried to attack dead monster {MonsterId}",
					player.PlayerId, monster.MonsterId );
				return null;
			}

			// 거리 검증
			if(!IsInAttackRange(player.Position, monster.Position))
			{
				_logger.LogWarning( "Player {PlayerId} out of attack range for monster {MonsterId}",
					player.PlayerId, monster.MonsterId );
				return null;
			}

			// 데미지 계산
			int attackPower = player.GetTotalAttack();
			int defense = monster.StaticData.Defense;
			var (damage, isCritical) = CalculateDamage( attackPower, defense );

			// 데미지 적용
			bool damaged = monster.TakeDamage(damage, player.PlayerId);
			if(!damaged)
			{
				_logger.LogWarning( "Failed to apply damage to monster {MonsterId}", monster.MonsterId );
				return null;
			}

			// 공격 쿨다운 시작
			player.StartAttackCooldown();
			_logger.LogInformation(
				 "Player {PlayerId} attacked Monster {MonsterId} for {Damage} damage (Critical: {IsCritical}) - Remaining HP: {CurrentHP}",
				 player.PlayerId, monster.MonsterId, damage, isCritical, monster.CurrentHP );

			// 결과 반환
			return new CombatResults
			{
				AttackerId = player.PlayerId,
				TargetId = monster.MonsterId,
				Damage = damage,
				IsCritical = isCritical,
				TargetCurrentHP = monster.CurrentHP,
				TargetDied = !monster.IsAlive
			};
		}
	}
}
