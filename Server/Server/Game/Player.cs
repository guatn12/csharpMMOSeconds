using Protocol;
using Server.Database.Entities;
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
		private readonly object _playerLock = new object();

		// 공격 쿨다운 (이동 제한용)
		private DateTime _lastAttackTime = DateTime.MinValue;
		private readonly TimeSpan _attackCooldown = TimeSpan.FromSeconds(1); // 공격 후 1초간 이동 불가

		// 인벤, 장비
		public PlayerInventory Inventory { get; private set; }
		public PlayerEquipment Equipment { get; private set; }

		// 이벤트 시스템
		public event Action<Player> OnLevelUp;
		public event Action<Player> OnDeath;
		public event Action<Player, int, int> OnHealthChanged; // (Player, oldHP, newHP)
		public event Action<Player, int, int> OnManaChanged;   // (Player, oldMP, newMP)
		public event Action<Player, PlayerState, PlayerState> OnStateChanged; // (Player, oldState, newState)

		// 인벤, 장비 관련 이벤트
		public event Action<Player, int, InventoryItem> OnItemAdded;									// 아이템 획득
		public event Action<Player, int, InventoryItem> OnItemRemoved;                                  // 아이템 제거
		public event Action<Player, PlayerEquipment.EquipSlot, InventoryItem> OnItemEquipped;			// 장비 착용
		public event Action<Player, PlayerEquipment.EquipSlot, InventoryItem> OnItemUnequipped;			// 장비 해제
		public event Action<Player, Dictionary<PlayerEquipment.StatType, int>> OnEquipmentStatsChanged; // 장비 스탯 변경
		
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

			Inventory = new PlayerInventory( playerId );
			Equipment = new PlayerEquipment( playerId );

			// 이벤트 구독 설정
			SetupCompositionEvents();
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
			lock(_playerLock)
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
			}
			
			UpdateLastUpdateTime();
			return true;
		}

		public bool Heal( int amount )
		{
			lock(_playerLock)
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
			lock(_playerLock)
			{
				if(State == newState) return;

				PlayerState oldState = State;
				Info.State = newState;

				// 상태 변경 이벤트 발생
				OnStateChanged?.Invoke( this, oldState, newState );
			}

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
			// 공격 쿨다운 중에는 이동 불가
			if(DateTime.UtcNow - _lastAttackTime < _attackCooldown)
				return false;

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

		// 공격 쿨다운 시작 (이동 제한)
		public void StartAttackCooldown()
		{
			_lastAttackTime = DateTime.UtcNow;
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

		// 인벤토리 관련 메서드
		public bool AddItem(int itemId, int quantity = 1, Dictionary<string, double> options = null)
		{

			return Inventory.AddItem( itemId, quantity, options );
		}

		public bool RemoveItem(int itemId, int quantity = 1)
		{
			return Inventory.RemoveItem( itemId, quantity );
		}

		public bool UseItem(int slot, int quantity = 1)
		{
			InventoryItem item = Inventory.GetItem( slot );
			if(item == null) return false;

			// 아이템 사용 효과 적용
			bool effectApplied = ApplyItemEffect(item, quantity);
			if(!effectApplied) return false;

			// 인벤토리에서 아이템 소모
			return Inventory.UseItem(slot, quantity);
		}

		public bool HasItem(int itemId, int quantity = 1)
		{
			return Inventory.HasItem( itemId, quantity );
		}

		public bool AddGold(long amount)
		{
			return Inventory.AddGold( amount );
		}

		public bool RemoveGold(long amount)
		{
			return Inventory.RemoveGold( amount );
		}

		public long GetGold()
		{
			return Inventory.Gold;
		}

		// 장비 관련
		public bool EquipItemFromInventory(int inventorySlot)
		{
			InventoryItem item = Inventory.GetItem(inventorySlot);
			if(item == null) return false;
			if(!Equipment.CanEquipItem( item.ItemId )) return false;

			// 인벤토리에서 아이템 제거
			if(!Inventory.RemoveItemFromSlot(item.Slot)) return false;

			// 기존 장비가 있다면 인벤토리로 이동
			var targetSlot = GetItemEquipSlot(item.ItemId);
			InventoryItem existingItem = Equipment.GetEquippedItem(targetSlot);
			if(existingItem != null)
			{
				if(!Inventory.HasSpace())
				{
					// 인벤토리 공간 부족 - 원래 아이템 복구
					Inventory.AddItem( item.ItemId, item.Quantity, item.Options );
					return false;
				}
				Equipment.UnequipItem( targetSlot );
				Inventory.AddItem( existingItem.ItemId, existingItem.Quantity, existingItem.Options );
			}

			// 새 장비 착용
			return Equipment.EquipItem( item );
		}

		public bool UnequipItemToInventory(PlayerEquipment.EquipSlot slot)
		{
			InventoryItem item = Equipment.GetEquippedItem(slot);
			if(item == null) return false;
			if(!Inventory.HasSpace()) return false;

			// 장비 해제
			var unequippedItem = Equipment.UnequipItemAndReturn(slot);
			if(unequippedItem == null) return false;

			// 인벤토리에 추가
			return Inventory.AddItem( unequippedItem.ItemId, unequippedItem.Quantity, unequippedItem.Options );
		}

		// 장비 스탯 조회 메서드들
		public int GetTotalAttack()
		{
			return Equipment.GetTotalAttack();
		}

		public int GetTotalDefense()
		{
			return Equipment.GetTotalDefense();
		}

		public float GetCriticalRate()
		{
			return Equipment.GetCriticalRate();
		}

		public int GetSpeed()
		{
			return Equipment.GetSpeed();
		}

		// 데이터 동기화 관련
		public bool HasDirtyData()
		{
			return Inventory.IsDirty || Equipment.IsDirty;
		}

		public void MarkDataClean()
		{
			Inventory.MarkClean();
			Equipment.MarkClean();
		}

		// 인벤 / 장비 데이터 로드 (DB / Redis에서 복원용)
		public void LoadInventoryData(InventoryModel inventoryModel)
		{
			Inventory.LoadFromInventoryModel(inventoryModel);
		}

		public void LoadEquipmentData(Dictionary<PlayerEquipment.EquipSlot, InventoryItem> equipmentData)
		{
			Equipment.LoadFromEquipmentData(equipmentData);
			RecalculatePlayerStats();
		}

		// 인벤 / 장비 저장(DB / Redis)
		public InventoryModel GetInventoryData()
		{
			return Inventory.ToInventoryModel();
		}

		public Dictionary<PlayerEquipment.EquipSlot, InventoryItem> GetEquipmentData()
		{
			return Equipment.ToEquipmentDictionary();
		}

		private void UpdateLastUpdateTime()
		{
			LastUpdateTime = DateTime.UtcNow;
		}

		private void SetupCompositionEvents()
		{
			// 인벤토리 이벤트 구독
			Inventory.OnItemAdded += ( inv, slot, item ) => OnItemAdded?.Invoke( this, slot, item );
			Inventory.OnItemRemoved += ( inv, slot, item ) => OnItemRemoved?.Invoke( this, slot, item );
			Inventory.OnGoldChanged += OnInventoryGoldChanged;
			Inventory.OnInventoryChanged += OnInventoryChanged;

			// 장비 이벤트 구독
			Equipment.OnItemEquipped += ( eq, slot, item ) => OnItemEquipped?.Invoke( this, slot, item );
			Equipment.OnItemUnEquipped += ( eq, slot, item ) => OnItemUnequipped?.Invoke( this, slot, item );
			Equipment.OnStatsChanged += ( eq, stats ) =>
			{
				OnEquipmentStatsChanged?.Invoke( this, stats );
				RecalculatePlayerStats();
			};
			Equipment.OnEquipmentChanged += OnEquipmentChanged;
		}

		private void OnInventoryGoldChanged(PlayerInventory inventory, long oldGold, long newGold )
		{
			// TODO : 골드 변경 처리 (UI, 로그 등)
		}

		private void OnInventoryChanged(PlayerInventory inventory)
		{
			// TODO : 인벤토리 변경 처리
		}

		private void OnEquipmentChanged(PlayerEquipment equipment)
		{
			// TODO : 장비 변경 처리.
		}

		private void RecalculatePlayerStats()
		{
			// 기본 스탯
			int baseHP = 100 + (Level - 1) * 10;
			int baseMP = 50 + (Level - 1) * 5;

			// 장비 스탯 추가
			int equipmentHP = Equipment.GetTotalHP();
			int equipmentMP = Equipment.GetTotalMP();

			// 최대 HP/MP 업데이트
			int oldMaxHP = Info.MaxHP;
			int oldMaxMP = Info.MaxMP;

			Info.MaxHP = baseHP + equipmentHP;
			Info.MaxMP = baseMP + equipmentMP;

			// 현재 HP/MP도 비례적으로 조정
			if(0 < oldMaxHP)
			{
				float hpRatio = (float)Info.CurrentHP / oldMaxHP;
				Info.CurrentHP = Math.Min(Info.MaxHP, (int)(Info.MaxHP * hpRatio));
			}

			if(0 < oldMaxMP)
			{
				float mpRatio = (float)Info.CurrentMP / oldMaxMP;
				Info.CurrentMP = Math.Min(Info.MaxMP, (int)(Info.MaxMP * mpRatio));
			}

			UpdateLastUpdateTime();
		}

		private bool ApplyItemEffect(InventoryItem item, int quantity)
		{
			// 아이템 ID에 따른 효과 적용(임시)
			return item.ItemId switch
			{
				100 => UseHealthPotion( quantity * 50 ),        // 체력 포션
				101 => UseManaPotion( quantity * 30 ),      // 마나 포션
				102 => UseFullHealthPotion(),               // 완전 회복 표션
				_ => true
			};
		}

		private bool UseHealthPotion( int healAmount )
		{
			if(MaxHP <= CurrentHP) return false;
			return Heal( healAmount );
		}

		private bool UseManaPotion(int manaAmount)
		{
			if(MaxMP <= CurrentMP) return false;

			int oldMP = CurrentMP;
			Info.CurrentMP = Math.Min(MaxMP, CurrentMP + manaAmount);

			OnManaChanged?.Invoke( this, oldMP, CurrentMP );
			UpdateLastUpdateTime();
			return true;
		}

		private bool UseFullHealthPotion()
		{
			if(MaxHP <= CurrentHP && MaxMP <= CurrentMP) return false;

			int oldHP = CurrentHP;
			int oldMP = CurrentMP;

			Info.CurrentHP = MaxHP;
			Info.CurrentMP = MaxMP;

			OnHealthChanged?.Invoke( this, oldHP, oldMP );
			OnManaChanged?.Invoke(this, oldMP, CurrentMP );
			UpdateLastUpdateTime();
			return true;
		}

		private PlayerEquipment.EquipSlot GetItemEquipSlot(int itemId)
		{
			return itemId switch
			{
				>= 1000 and < 2000 => PlayerEquipment.EquipSlot.Weapon,
				>= 2000 and < 3000 => PlayerEquipment.EquipSlot.Shield,
				>= 3000 and < 4000 => PlayerEquipment.EquipSlot.Helmet,
				>= 4000 and < 5000 => PlayerEquipment.EquipSlot.Armor,
				>= 5000 and < 6000 => PlayerEquipment.EquipSlot.Gloves,
				>= 6000 and < 7000 => PlayerEquipment.EquipSlot.Boots,
				>= 7000 and < 8000 => PlayerEquipment.EquipSlot.Ring1,
				>= 8000 and < 9000 => PlayerEquipment.EquipSlot.Necklace,
				>= 9000 and < 10000 => PlayerEquipment.EquipSlot.Earring,
				_ => PlayerEquipment.EquipSlot.None
			};
		}
	}
}
