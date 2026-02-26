using Protocol;
using Server.Data.Models;
using Server.Game;
using Server.Game.Monsters;
using Server.Services.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.Combat
{
	/// <summary>
	/// 전투 로직을 담당하는 Service 인터페이스
	/// </summary>
	public interface ICombatService
	{
		/// <summary>
		/// 플레이어가 몬스터를 공격합니다.
		/// </summary>
		Task<CombatResults> ProcessPlayerAttackMonsterAsync( Player player, Monster monster, SkillData skillData );
		
		/// <summary>
		/// 데미지를 계산
		/// </summary>
		(int damage, bool isCritical) CalculateDamage( int attackPower, int defense, int criticalRate = 10 );

		/// <summary>
		/// 공격 범위 내에 있는지 확인합니다.
		/// </summary>
		bool IsInAttackRange( PosInfo attackerPos, PosInfo targetPos, float range = 5.0f );
	}
}
