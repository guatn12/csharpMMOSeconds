using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data.Models
{
	public class MonsterData
	{
		public int Id { get; set; }
		public string Name { get; set; } = string.Empty;
		public int Health { get; set; }
		public int Attack { get; set; }
		public int Defense { get; set; }
		public int ExpReward { get; set; }	// 경험치 보상
		public int GoldReward { get; set; }	// 골드 보상
		public float MoveSpeed { get; set; } = 1.0f;

		public string MonsterType { get; set; } = "Normal"; // 몬스터 등급 - Normal, Elite, Boss
		public int Level { get; set; } = 1;
		public List<int> Skills { get; set; } = new(); // 사용할 수 있는 스킬 ID

		// AI 관련 개체별 설정
		public float DetectRange { get; set; } = 10.0f;	// 플레이어 감지 범위
		public float AttackRange { get; set; } = 2.0f;		// 공격 범위
		public float ChaseRange { get; set; } = 15.0f;		// 추격 최대 범위
		public float ReturnRange { get; set; } = 25.0f;	// 귀환 시작 거리
		public float PatrolRadius { get; set; } = 5.0f;	// 배회 반경
		public float RespawnTime { get; set; } = 5.0f;		// 리스폰 시간 (초)

		// 데이터 검증 메서드
		public bool IsValid()
		{
			return Id > 0 &&
				!string.IsNullOrEmpty( Name ) &&
				Health > 0 &&
				Attack >= 0 &&
				Defense >= 0 &&
				ExpReward >= 0 &&
				GoldReward >= 0 &&
				MoveSpeed > 0 &&
				Level > 0 &&
				DetectRange > 0 &&
				AttackRange > 0 &&
				ChaseRange > 0 &&
				ReturnRange > 0 &&
				RespawnTime >= 0;
		}
	}
}
