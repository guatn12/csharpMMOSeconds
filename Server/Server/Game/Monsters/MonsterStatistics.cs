using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game.Monsters
{
	/// <summary>
	/// 몬스터 통계 정보 클래스
	/// </summary>
	public class MonsterStatistics
	{
		/// <summary>
		/// 전체 몬스터 수 (살아있는 + 죽은 몬스터)
		/// </summary>
		public int TotalMonsters { get; set; }

		/// <summary>
		/// 살아있는 몬스터 수
		/// </summary>
		public int AliveMonsters { get; set; }

		/// <summary>
		/// 죽은 몬스터 수
		/// </summary>
		public int DeadMonsters { get; set; }

		/// <summary>
		/// 전투 중인 몬스터 수
		/// </summary>
		public int MonstersInCombat { get; set; }

		/// <summary>
		/// 스폰 포인트 수
		/// </summary>
		public int SpawnPointCount { get; set; }

		/// <summary>
		/// 평균 HP 퍼센트 (0.0 ~ 1.0)
		/// </summary>
		public float AverageHP { get; set; }

		public override string ToString()
		{
			return $"Total: {TotalMonsters}, Alive: {AliveMonsters}, Dead: {DeadMonsters}, " +
					   $"InCombat: {MonstersInCombat}, SpawnPoints: {SpawnPointCount}, AvgHP: {AverageHP: P1}";
		}
	}
}
