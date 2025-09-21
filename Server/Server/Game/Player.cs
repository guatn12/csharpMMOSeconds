using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game
{
	public class Player
	{
		// 플레이어 데이터
		public PlayerInfo Info { get; private set; }
		public DateTime LastUpdateTime { get; private set; }
		
		private long _combatTargetId = 0;
		private readonly Dictionary<int, DateTime> _skillCooldowns = new Dictionary<int, DateTime>();

		// 이벤트 시스템
		public event Action<Player> OnLevelUp;
		public event Action<Player> OnDeath;
		public event Action<Player, int, int> OnHealthChanged; // (Player, oldHP, newHP)
		public event Action<Player, int, int> OnManaChanged;   // (Player, oldMP, newMP)
		public event Action<Player, PlayerState, PlayerState> OnStateChanged; // (Player, oldState, newState)

		public Player( long playerId, string playerName )
		{
			Info = new PlayerInfo
			{
				PlayerId = playerId,
				Name = playerName ?? $"Player_{playerId}",
				PosInfo = new PosInfo { PosX = 0, PosY = 0, PosZ = 0 },
				Level = 1,
				CurrentHP = 100,
				MaxHP = 100,
				CurrentMP = 50,
				MaxMP = 50,
				Experience = 0,
				State = PlayerState.Idle
			};

			LastUpdateTime = DateTime.UtcNow;
			_combatTargetId = 0;
		}

		public long PlayerId => Info.PlayerId;
		public string PlayerName => Info.Name;
		public PosInfo Position => Info.PosInfo;
		public int Level => Info.Level;
		public int CurrentHP => Info.CurrentHP;
		public int MaxHP => Info.MaxHP;
		public int CurrentMP => Info.CurrentMP;
		public int MaxMP => Info.MaxMP;
		public PlayerState State => Info.State;
		public long Experience => Info.Experience;

		public bool IsAlive => Info.State != PlayerState.Dead;
		public float HPPercentage => 0 < Info.MaxHP ? (float)Info.CurrentHP : 0f;
		public float MPPercentage => 0 < Info.MaxMP ? (float)Info.CurrentMP : 0f;
		public long RequiredExp => Info.Level * 100; // 임시 레벨업 필요 경험치.
		public long CombatTargetId => _combatTargetId;

		// 상태 관리 메서드
		public void UpdatePosition( PosInfo newPosition )
		{
			if(newPosition == null) return;

			Info.PosInfo = newPosition;
			SetState( PlayerState.Walking );
			UpdateLastUpdateTime();
		}

		public bool TakeDamage( int damage )
		{
			if(!IsAlive || damage <= 0) return false;

			int oldHP = CurrentHP;
			Info.CurrentHP = Math.Max( 0, CurrentHP - damage );

			// HP 변경 이벤트 발생
			OnHealthChanged?.Invoke( this, oldHP, CurrentHP );

			if(CurrentHP <= 0)
			{
				SetState( PlayerState.Dead );
				// 사망 이벤트 발생
				OnDeath?.Invoke( this );
			}

			UpdateLastUpdateTime();
			return true;
		}

		public bool Heal( int amount )
		{
			if(!IsAlive || amount <= 0) return false;

			int oldHP = CurrentHP;
			Info.CurrentHP = Math.Min( MaxHP, CurrentHP + amount );

			// HP 변경 이벤트 발생
			OnHealthChanged?.Invoke( this, oldHP, CurrentHP );

			if(State == PlayerState.Dead && 0 < CurrentHP)
			{
				SetState( PlayerState.Idle );
			}

			LastUpdateTime = DateTime.UtcNow;
			return true;
		}

		public bool GainExperience( long exp )
		{
			if(exp <= 0) return false;

			Info.Experience += exp;

			// 레벨업 체크
			bool levelUp = false;
			while(RequiredExp <= Experience && Level < 100) // 최대 레벨 100 제한
			{
				Info.Experience -= RequiredExp;
				Info.Level++;
				levelUp = true;

				// 레벨업 시 스탯 증가
				int oldHP = CurrentHP;
				int oldMP = CurrentMP;
				Info.MaxHP += 10;
				Info.MaxMP += 5;
				Info.CurrentHP = MaxHP;
				Info.CurrentMP = MaxMP;

				// 레벨업 이벤트 발생
				OnLevelUp?.Invoke( this );

				// HP/MP 변경 이벤트 발생
				OnHealthChanged?.Invoke( this, oldHP, CurrentHP );
				OnManaChanged?.Invoke( this, oldMP, CurrentMP );
			}

			UpdateLastUpdateTime();
			return levelUp;
		}

		public void SetState( PlayerState newState )
		{
			if(State == newState) return;

			PlayerState oldState = State;
			Info.State = newState;

			// 상태 변경 이벤트 발생
			OnStateChanged?.Invoke( this, oldState, newState );

			LastUpdateTime = DateTime.UtcNow;
		}

		public void RestoreToIdleIfMoving()
		{
			if(State == PlayerState.Walking || State == PlayerState.Running)
			{
				SetState( PlayerState.Idle );
			}
		}

		public void Disconnect()
		{
			SetState( PlayerState.Disconnected );
		}

		// 디버깅용 메서드
		public override string ToString()
		{
			return $"Player[{PlayerId}:{PlayerName}] Lv.{Level} HP:{CurrentHP}/{MaxHP} " +
				$"State:{State}Pos: ({Position?.PosX},{Position?.PosY},{Position?.PosZ})";
		}

		// 상태 검증 메서드들
		public bool IsValidState()
		{
			return Enum.IsDefined( typeof( PlayerState ), State );
		}

		public bool IsValidStats()
		{
			return 0 <= CurrentHP && CurrentHP <= MaxHP &&
				0 <= CurrentMP && CurrentMP <= MaxMP &&
				1 <= Level && Level <= 100 &&
				0 <=Experience;
		}

		public bool CanPerformAction()
		{
			return IsAlive && State != PlayerState.Disconnected;
		}

		public bool CanMove()
		{
			return CanPerformAction() && State != PlayerState.Combat;
		}

		// 고급 상태 관리 메서드
		public void EnterCombat( Player target = null )
		{
			if(!CanPerformAction()) return;

			_combatTargetId = target.PlayerId;
			SetState( PlayerState.Combat );
		}

		public void ExitCombat()
		{
			if(State == PlayerState.Combat)
			{
				_combatTargetId = 0;	
				SetState( PlayerState.Idle );
			}
		}

		public bool CanUseSkill( int skillId )
		{
			if(!CanPerformAction()) return false;

			// 기본 스킬 사용 조건 검증
			if(State == PlayerState.Dead || State == PlayerState.Disconnected) return false;

			// MP 체크 (임시 - 추후 스킬 데이터로 확장)
			int requiredMP = skillId * 10; // 임시 MP 계산
			if(CurrentMP < requiredMP) return false;

			return true;
		}

		public bool UseSkill( int skillId, int mpCost = 0 )
		{
			if(!CanUseSkill( skillId )) return false;

			// MP 소모 (매개변수가 0이면 기본 계산 사용)
			int actualMpCost = mpCost > 0 ? mpCost : skillId * 10;
			int oldMP = CurrentMP;
			Info.CurrentMP = Math.Max( 0, CurrentMP - actualMpCost );

			// MP 변경 이벤트 발생
			OnManaChanged?.Invoke( this, oldMP, CurrentMP );

			UpdateLastUpdateTime();
			return true;
		}

		public bool IsSkillOnCooldown(int skillId )
		{
			if(!_skillCooldowns.TryGetValue( skillId, out DateTime cooldownEnd ))
				return false;

			return DateTime.UtcNow < cooldownEnd;
		}

		public void SetSkillCooldown(int skillId, TimeSpan cooldown )
		{
			if(!_skillCooldowns.TryAdd( skillId, DateTime.UtcNow.Add( cooldown ) ))
			{
				throw new ArgumentException( $"{skillId} cooldown add failed." );
			}
		}

		private void UpdateLastUpdateTime()
		{
			LastUpdateTime = DateTime.UtcNow;
		}
	}
}
