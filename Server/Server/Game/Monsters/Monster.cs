using Protocol;
using Server.Data.Models;
using Server.Game.Objects;
using Server.Utils;
using System;

namespace Server.Game.Monsters
{
	public class Monster : GameObject
	{
		// 정적 데이터 참조
		public MonsterData StaticData { get; private set; }
		public MonsterDetailInfo DetailInfo { get; private set; }
		// 스폰 정보
		public PosInfo SpawnPosition { get; private set; }
		public DateTime SpawnTime { get; private set; }

		// AI 타겟
		private long _targetPlayerId = 0;

		// 속성
		public int TemplateId => StaticData.Id;
		public long TargetPlayerId => _targetPlayerId;
		public bool IsInCombat => CreatureState == State.InCombat || CreatureState == State.Chase;
		public float HPPercentage => 0 < MaxHP ? (float)CurrentHP / MaxHP : 0f;

		public Monster(long monsterId, int templatedId, PosInfo spawnPos, MonsterData staticData)
			:base(monsterId, ObjectType.ObjectMonster)
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

			DetailInfo = new MonsterDetailInfo { TemplatedId = templatedId };

			_name = StaticData.Name;
			_posInfo = new PosInfo
			{
				PosX = spawnPos.PosX,
				PosY = spawnPos.PosY,
				PosZ = spawnPos.PosZ,
				RotationX = spawnPos.RotationX,
				RotationY = spawnPos.RotationY,
				RotationZ = spawnPos.RotationZ,
			};

			_statInfo.CurrentHP = StaticData.Health;
			_statInfo.MaxHP = StaticData.Health;
			_statInfo.Level = StaticData.Level;
			_statInfo.Speed = StaticData.MoveSpeed;
			_statInfo.Attack = StaticData.Attack;
			_statInfo.Defense = StaticData.Defense;
			_creatureState = State.Idle;

			SpawnTime = DateTime.UtcNow;
			_targetPlayerId = 0;
		}

		// 위치 업데이트
		public override void UpdatePosition(PosInfo newPosition)
		{
			if(newPosition == null || !IsAlive) return;

			PosInfo.PosX = newPosition.PosX;
			PosInfo.PosY = newPosition.PosY;
			PosInfo.PosZ = newPosition.PosZ;
			PosInfo.RotationX = newPosition.RotationX;
			PosInfo.RotationY = newPosition.RotationY;
			PosInfo.RotationZ = newPosition.RotationZ;

			UpdateLastUpdateTime();
		}

		public override bool TakeDamage(int damage, long attackerId)
		{
			lock(_lock)
			{
				if(!IsAlive || damage <= 0) return false;

				int oldHP = Stats.CurrentHP;
				Stats.CurrentHP = Math.Max( 0, Stats.CurrentHP - damage );
				RaiseOnHealthChanged(oldHP, Stats.CurrentHP);

				// 타겟 설정
				if(_targetPlayerId == 0 && 0 < attackerId)
				{
					SetTarget( attackerId );
				}

				// 사망 처리.
				if(Stats.CurrentHP <= 0)
				{
					SetState( State.Dead );
					RaiseOnDeath();
				}
			}

			UpdateLastUpdateTime();
			return true;
		}

		public override bool Heal(int amount)
		{
			lock(_lock)
			{
				if(!IsAlive || amount <= 0 || Stats.MaxHP <= Stats.CurrentHP) return false;

				int oldHP = CurrentHP;
				Stats.CurrentHP = Math.Min( Stats.MaxHP, Stats.CurrentHP + amount );
				RaiseOnHealthChanged( oldHP, Stats.CurrentHP );
			}


			UpdateLastUpdateTime();
			return true;
		}

		// 최대 회복(스폰 위치 귀환 시)
		public void Restore()
		{
			lock(_lock)
			{
				int oldHP = CurrentHP;
				Stats.CurrentHP = MaxHP;

				RaiseOnHealthChanged( oldHP, Stats.CurrentHP );

				// 타겟 초기화
				ClearTarget();
			}

			UpdateLastUpdateTime();
		}

		public void SetTarget(long playerId)
		{
			_targetPlayerId = playerId;
		}

		public float GetDistanceToSpawn()
		{
			return Position3DValidator.CalculateDistance3D( PosInfo, SpawnPosition );
		}

		public void ClearTarget()
		{
			_targetPlayerId = 0;
		}

		private bool CanAttack()
		{
			return IsAlive && CreatureState == State.InCombat && 0 < _targetPlayerId;
		}

		public override ObjectInfo ToObjectInfo()
		{
			return new ObjectInfo
			{
				PosInfo = PosInfo.Clone(),
				ObjectId = ObjectId,
				Type = Type,
				Name = Name,
				State = CreatureState,
				StatInfo = Stats.Clone(),
				MonsterDetailInfo = DetailInfo
			};
		}
	}
}
