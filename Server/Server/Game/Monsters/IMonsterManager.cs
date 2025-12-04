using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game.Monsters
{
	/// <summary>
	/// 몬스터 관리자 인터페이스
	/// Room의 몬스터 생명주기 및 상태를 중앙 집중식으로 관리
	/// </summary>
	public interface IMonsterManager : IDisposable
	{
		#region Events (이벤트)

		/// <summary>
		/// 몬스터가 Despawn되었을 때 발생 (S_MonsterDespawn 브로드캐스트용)
		/// </summary>
		event Action<long> OnMonsterDespawned;

		/// <summary>
		/// 몬스터가 Spawn되었을 때 발생 (S_MonsterSpawn 브로드캐스트용)
		/// </summary>
		event Action<Monster> OnMonsterSpawned;

		#endregion

		#region Lifecycle Management (생명주기 관리)

		/// <summary>
		/// MonsterManager 초기화 (Room 생성 시 호출)
		/// </summary>
		Task InitializeAsync();

		/// <summary>
		/// 스폰 포인트 추가 (BaseRoom에서 호출)
		/// </summary>
		void AddSpawnPoint( int templateId, PosInfo position, TimeSpan? respawnInterval = null );

		/// <summary>
		/// 초기 몬스터 스폰 (스폰 포인트 설정 후 호출)
		/// </summary>
		void SpawnInitialMonsters();

		/// <summary>
		/// 특정 위치에 몬스터 스폰
		/// </summary>
		Monster SpawnMonster( int templateId, PosInfo position );

		/// <summary>
		/// 몬스터 제거 (즉시 또는 딜레이)
		/// </summary>
		void DespawnMonster( long monsterId, TimeSpan? delay = null );

		/// <summary>
		/// 몬스터 AI 및 리스폰 주기적 업데이트 (JobQueue에서 호출)
		/// </summary>
		void Update();

		#endregion

		#region High-Level Search APIs (고수준 검색 API)

		/// <summary>
		/// 특정 범위 내 모든 몬스터 조회
		/// </summary>
		List<Monster> GetMonstersInRange( PosInfo center, float radius );

		/// <summary>
		/// 가장 가까운 몬스터 찾기 (조건 필터 가능)
		/// </summary>
		Monster FindNearestMonster( PosInfo position, Func<Monster, bool> predicate = null );

		/// <summary>
		/// 특정 템플릿 ID의 모든 몬스터 조회
		/// </summary>
		List<Monster> GetMonstersByTemplateId( int templateId );

		/// <summary>
		/// 전투 중인 몬스터만 조회
		/// </summary>
		List<Monster> GetMonstersInCombat();

		/// <summary>
		/// 특정 몬스터 조회
		/// </summary>
		Monster GetMonster( long monsterId );

		/// <summary>
		/// 살아있는 몬스터만 조회
		/// </summary>
		List<Monster> GetAliveMonsters();

		#endregion

		#region Centralized State Management (중앙 집중식 상태 관리)

		/// <summary>
		/// 몬스터 HP 업데이트
		/// </summary>
		bool UpdateMonsterHP( long monsterId, int newHP );

		/// <summary>
		/// 몬스터 타겟 설정
		/// </summary>
		bool SetMonsterTarget( long monsterId, long targetPlayerId );

		/// <summary>
		/// 특정 플레이어를 타겟으로 하는 모든 몬스터의 타겟 해제
		/// </summary>
		void ClearTargetsForPlayer( long playerId );

		#endregion

		#region AI Control (AI 제어)

		/// <summary>
		/// 모든 몬스터 AI 일시정지
		/// </summary>
		void PauseAllMonsterAI();

		/// <summary>
		/// 모든 몬스터 AI 재개
		/// </summary>
		void ResumeAllMonsterAI();

		#endregion

		#region Policy Management (정책 관리)

		/// <summary>
		/// 스폰 정책 설정
		/// </summary>
		void SetSpawnPolicy( MonsterSpawnPolicy policy );

		/// <summary>
		/// 최대 몬스터 수 설정
		/// </summary>
		void SetMaxMonsters( int maxCount );

		#endregion

		#region Statistics (통계)

		/// <summary>
		/// 몬스터 통계 조회
		/// </summary>
		MonsterStatistics GetStatistics();

		/// <summary>
		/// 현재 몬스터 수 조회
		/// </summary>
		int GetMonsterCount();

		#endregion
	}
}
