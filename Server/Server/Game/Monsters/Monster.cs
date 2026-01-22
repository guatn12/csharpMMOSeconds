using Protocol;
using Server.Data.Models;
using Server.Utils;
using System;

namespace Server.Game.Monsters
{
	public class Monster
	{
		public MonsterInfo Info { get; private set; }

		// 정적 데이터 참조
		public MonsterData StaticData { get; private set; }

		private static int _monsterNextId = 0;

		// 스폰 정보
		public PosInfo SpawnPosition { get; private set; }
		public DateTime SpawnTime { get; private set; }
		public DateTime LastUpdateTime { get; private set; }

		// AI 타겟
		private long _targetPlayerId = 0;

		// 몬스터 락
		private readonly object _monsterLock = new object();

		// 이벤트
		public event Action<Monster> OnDeath;										// 사망 처리 이벤트
		public event Action<Monster, int, int> OnHealthChanged;						// 체력 변경 이벤트
		public event Action<Monster, MonsterState, MonsterState> OnStateChanged;	// 상태 변경 이벤트

		// 속성
		public long MonsterId => Info.MonsterId;
		public int TemplateId => Info.TemplateId;
		public string Name => Info.Name;
		public PosInfo Position => Info.PosInfo;
		public int Level => Info.Level;
		public int CurrentHP => Info.CurrentHP;
		public int MaxHP => Info.MaxHP;
		public MonsterState State => Info.State;
		public long TargetPlayerId => _targetPlayerId;

		public bool IsAlive => Info.State != MonsterState.MonsterDie && 0 < Info.CurrentHP;
		public bool IsInCombat => Info.State == MonsterState.MonsterAttack ||
			Info.State == MonsterState.MonsterChase;
		public float HPPercentage => 0 < MaxHP ? (float)CurrentHP / MaxHP : 0f;

		public Monster(long monsterId, int templatedId, PosInfo spawnPos, MonsterData staticData)
		{
			if (staticData == null)
			{
				throw new ArgumentNullException(nameof(templatedId));
			}
			StaticData = staticData;
			SpawnPosition = new PosInfo
			{
				PosX = spawnPos.PosX,
				PosY = spawnPos.PosY,
				PosZ = spawnPos.PosZ,
				RotationX = spawnPos.RotationX,
				RotationY = spawnPos.RotationY,
				RotationZ = spawnPos.RotationZ,
			};

			Info = new MonsterInfo
			{
				MonsterId = monsterId,
				TemplateId = templatedId,
				Name = staticData.Name,
				PosInfo = new PosInfo
				{
					PosX = spawnPos.PosX,
					PosY = spawnPos.PosY,
					PosZ = spawnPos.PosZ,
					RotationX = spawnPos.RotationX,
					RotationY = spawnPos.RotationY,
					RotationZ = spawnPos.RotationZ,
				},
				Level = staticData.Level,
				CurrentHP = staticData.Health,
				MaxHP = staticData.Health,
				State = MonsterState.MonsterIdle,
			};

			SpawnTime = DateTime.UtcNow;
			LastUpdateTime = DateTime.UtcNow;
			_targetPlayerId = 0;
		}

		// 위치 업데이트
		public void UpdatePosition(PosInfo newPosition)
		{
			if(newPosition == null || !IsAlive) return;

			Info.PosInfo.PosX = newPosition.PosX;
			Info.PosInfo.PosY = newPosition.PosY;
			Info.PosInfo.PosZ = newPosition.PosZ;
			Info.PosInfo.RotationX = newPosition.RotationX;
			Info.PosInfo.RotationY = newPosition.RotationY;
			Info.PosInfo.RotationZ = newPosition.RotationZ;

			LastUpdateTime = DateTime.UtcNow;
		}

		public bool TakeDamage(int damage, long attackerId)
		{
			lock(_monsterLock)
			{
				if(!IsAlive || damage <= 0) return false;

				int oldHP = Info.CurrentHP;
				Info.CurrentHP = Math.Max( 0, Info.CurrentHP - damage );
				OnHealthChanged?.Invoke( this, oldHP, Info.CurrentHP );

				// 타겟 설정
				if(_targetPlayerId == 0 && 0 < attackerId)
				{
					SetTarget( attackerId );
				}

				// 사망 처리.
				if(Info.CurrentHP <= 0)
				{
					UpdateState( MonsterState.MonsterDie );
					OnDeath?.Invoke( this );
				}
			}

			LastUpdateTime = DateTime.UtcNow;
			return true;
		}

		public bool Heal(int amount)
		{
			lock(_monsterLock)
			{
				if(!IsAlive || amount <= 0 || Info.MaxHP <= Info.CurrentHP) return false;

				int oldHP = CurrentHP;
				Info.CurrentHP = Math.Min( Info.MaxHP, Info.CurrentHP + amount );
				OnHealthChanged?.Invoke( this, oldHP, Info.CurrentHP );
			}
			

			LastUpdateTime = DateTime.UtcNow;
			return true;
		}

		// 최대 회복(스폰 위치 귀환 시)
		public void Restore()
		{
			lock(_monsterLock)
			{
				int oldHP = CurrentHP;
				Info.CurrentHP = MaxHP;

				OnHealthChanged?.Invoke( this, oldHP, CurrentHP );

				// 타겟 초기화
				ClearTarget();
			}

			LastUpdateTime = DateTime.UtcNow;
		}

		public void SetTarget(long playerId)
		{
			_targetPlayerId = playerId;
		}

		public void UpdateState( MonsterState newState )
		{
			lock(_monsterLock)
			{
				if(State == newState) return;

				MonsterState oldState = State;
				Info.State = newState;

				OnStateChanged( this, oldState, newState );
			}
			
			LastUpdateTime = DateTime.UtcNow;
		}

		public float GetDistanceToSpawn()
		{
			return Position3DValidator.CalculateDistance3D( Position, SpawnPosition );
		}

		public void ClearTarget()
		{
			_targetPlayerId = 0;
		}

		private bool CanAttack()
		{
			return IsAlive && State == MonsterState.MonsterAttack && 0 < _targetPlayerId;
		}
	}
}
