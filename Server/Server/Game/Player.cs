using Protocol;
using Server.Database.Entities;
using Server.Game.Objects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Game
{
	public class Player : GameObject
	{
		// 플레이어 데이터
		private long _combatTargetId = 0;
		private readonly Dictionary<int, DateTime> _skillCooldowns = new Dictionary<int, DateTime>();

		// 공격 쿨다운 (이동 제한용)
		private DateTime _lastAttackTime = DateTime.MinValue;
		private readonly TimeSpan _attackCooldown = TimeSpan.FromSeconds(1); // 공격 후 1초간 이동 불가

		// 인벤, 장비
		public PlayerInventory Inventory { get; private set; }
		public PlayerEquipment Equipment { get; private set; }

		// 이벤트 시스템
		public event Action<Player> OnLevelUp;
		public event Action<Player, int, int> OnManaChanged;   // (Player, oldMP, newMP)

		// 인벤, 장비 관련 이벤트
		public event Action<Player, int, InventoryItem> OnItemAdded;									// 아이템 획득
		public event Action<Player, int, InventoryItem> OnItemRemoved;                                  // 아이템 제거
		public event Action<Player, PlayerEquipment.EquipSlot, InventoryItem> OnItemEquipped;			// 장비 착용
		public event Action<Player, PlayerEquipment.EquipSlot, InventoryItem> OnItemUnequipped;			// 장비 해제
		public event Action<Player, Dictionary<PlayerEquipment.StatType, int>> OnEquipmentStatsChanged; // 장비 스탯 변경
		
		public Player( long playerRawId, string playerName )
			:base(GameObjectId.Generate(ObjectType.ObjectPlayer, playerRawId ), ObjectType.ObjectPlayer)
		{
			_name = playerName ?? $"Player_{playerRawId}";
			_posInfo = new PosInfo { PosX = 0, PosY = 0, PosZ = 0 };
			_statInfo.Level = 1;
			_statInfo.CurrentHP = 100;
			_statInfo.CurrentMP = 100;
			_statInfo.MaxHP = 100;
			_statInfo.MaxMP = 100;
			_statInfo.Experience = 0;
			_statInfo.Attack = 10;
			_statInfo.Defense = 10;
			_statInfo.Speed = 5.0f;
			_creatureState = State.Idle;

			_lastUpdateTime = DateTime.UtcNow;
			_combatTargetId = 0;

			Inventory = new PlayerInventory( playerRawId );
			Equipment = new PlayerEquipment( playerRawId );

			// 이벤트 구독 설정
			SetupCompositionEvents();
		}

		public long Experience => Stats.Experience;
		public float HPPercentage => 0 < Stats.MaxHP ? (float)Stats.CurrentHP / MaxHP : 0f;
		public float MPPercentage => 0 < Stats.MaxMP ? (float)Stats.CurrentMP / MaxMP : 0f;
		public long RequiredExp => Stats.Level * 100; // 임시 레벨업 필요 경험치.
		public long CombatTargetId => _combatTargetId;

		public void InitPosition(PosInfo newPosInfo)
		{
			if(newPosInfo == null) return;

			_posInfo = newPosInfo;
			SetState( State.Idle );
			UpdateLastUpdateTime();
		}

		// 상태 관리 메서드
		public override void UpdatePosition( PosInfo newPosition )
		{
			base.UpdatePosition( newPosition );
			SetState( State.Walking );
		}

		public override bool TakeDamage( int damage, long attackerId )
		{
			lock(_lock)
			{
				if(!IsAlive || damage <= 0) return false;

				int oldHP = CurrentHP;
				Stats.CurrentHP = Math.Max( 0, CurrentHP - damage );

				// HP 변경 이벤트 발생
				RaiseOnHealthChanged( oldHP, CurrentHP );

				if(CurrentHP <= 0)
				{
					SetState( State.Dead );
					// 사망 이벤트 발생
					RaiseOnDeath();
				}
			}
			
			UpdateLastUpdateTime();
			return true;
		}

		public override bool Heal( int amount )
		{
			lock(_lock)
			{
				if(!IsAlive || amount <= 0) return false;

				int oldHP = CurrentHP;
				Stats.CurrentHP = Math.Min( MaxHP, CurrentHP + amount );

				// HP 변경 이벤트 발생
				RaiseOnHealthChanged(oldHP, CurrentHP );
			}
			

			UpdateLastUpdateTime();
			return true;
		}

		public bool GainExperience( long exp )
		{
			if(exp <= 0) return false;

			Stats.Experience += exp;

			// 레벨업 체크
			bool levelUp = false;
			while(RequiredExp <= Experience && Level < 100) // 최대 레벨 100 제한
			{
				Stats.Experience -= RequiredExp;
				Stats.Level++;
				levelUp = true;

				// 레벨업 시 스탯 증가
				int oldHP = CurrentHP;
				int oldMP = CurrentMP;
				Stats.MaxHP += 10;
				Stats.MaxMP += 5;
				Stats.CurrentHP = MaxHP;
				Stats.CurrentMP = MaxMP;

				// 레벨업 이벤트 발생
				OnLevelUp.Invoke( this );

				// HP/MP 변경 이벤트 발생
				RaiseOnHealthChanged( oldHP, CurrentHP );
				OnManaChanged.Invoke(this, oldMP, CurrentMP );
			}

			UpdateLastUpdateTime();
			return levelUp;
		}

		//public void RestoreToIdleIfMoving()
		//{
		//	if(State == PlayerState.Walking || State == PlayerState.Running)
		//	{
		//		SetState( PlayerState.Idle );
		//	}
		//}

		public void Revive()
		{
			if(IsAlive) return;
			Stats.CurrentHP = MaxHP; // 부활 시 체력 회복
			Stats.CurrentMP = MaxMP; // 부활 시 마나 회복
			SetState( State.Idle );
		}

		//public void Disconnect()
		//{
		//	SetState( PlayerState.Disconnected );
		//}

		// 디버깅용 메서드
		public override string ToString()
		{
			return $"Player[{ObjectId}:{_name}] Lv.{Level} HP:{CurrentHP}/{MaxHP} " +
				$"State:{_creatureState} Pos: ({PosInfo.PosX},{PosInfo.PosY},{PosInfo.PosZ})";
		}

		// 상태 검증 메서드들
		//public bool IsValidState()
		//{
		//	return Enum.IsDefined( typeof( PlayerState ), State );
		//}

		//public bool IsValidStats()
		//{
		//	return 0 <= CurrentHP && CurrentHP <= MaxHP &&
		//		0 <= CurrentMP && CurrentMP <= MaxMP &&
		//		1 <= Level && Level <= 100 &&
		//		0 <=Experience;
		//}

		//public bool CanPerformAction()
		//{
		//	return IsAlive;
		//}

		public bool CanMove()
		{
			// 공격 쿨다운 중에는 이동 불가
			if(DateTime.UtcNow - _lastAttackTime < _attackCooldown)
				return false;

			return IsAlive && CreatureState != State.InCombat;
		}

		// 고급 상태 관리 메서드
		public void EnterCombat( Player target = null )
		{
			if(!IsAlive) return;

			if(target == null) return;

			_combatTargetId = target.ObjectId;
			SetState( State.InCombat );
		}

		public void ExitCombat()
		{
			if(CreatureState == State.InCombat)
			{
				_combatTargetId = 0;
				SetState( State.Idle );
			}
		}

		// 공격 쿨다운 시작 (이동 제한)
		public void StartAttackCooldown()
		{
			_lastAttackTime = DateTime.UtcNow;
		}

		public bool CanUseSkill( int skillId )
		{
			if(!IsAlive) return false;

			// 기본 스킬 사용 조건 검증
			if(CreatureState == State.Dead) return false;

			// MP 체크 (임시 - 추후 스킬 데이터로 확장)
			//int requiredMP = 10; // 임시 MP 계산
			//if(CurrentMP < requiredMP) return false;

			return true;
		}

		public bool UseSkill( int skillId, int mpCost = 0 )
		{
			if(!CanUseSkill( skillId )) return false;

			// MP 소모 (매개변수가 0이면 기본 계산 사용)
			int actualMpCost = mpCost > 0 ? mpCost : skillId * 10;
			int oldMP = CurrentMP;
			Stats.CurrentMP = Math.Max( 0, CurrentMP - actualMpCost );

			// MP 변경 이벤트 발생
			OnManaChanged.Invoke( this, oldMP, CurrentMP );

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

		public float GetSpeed()
		{
			return _statInfo.Speed + (float)Equipment.GetSpeed();
		}

		public StatInfo GetStatInfo()
		{
			return new StatInfo
			{
				Speed = _statInfo.Speed + (float)Equipment.GetSpeed(),
				Level = _statInfo.Level,
				Experience = _statInfo.Experience,
				Attack = _statInfo.Attack + Equipment.GetTotalAttack(),
				Defense = _statInfo.Defense + Equipment.GetTotalDefense(),
				CurrentHP = _statInfo.CurrentHP,
				CurrentMP = _statInfo.CurrentMP,
				MaxHP = _statInfo.MaxHP + Equipment.GetTotalHP(),
				MaxMP = _statInfo.MaxMP + Equipment.GetTotalMP(),
			};
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
			int oldMaxHP = Stats.MaxHP;
			int oldMaxMP = Stats.MaxMP;

			Stats.MaxHP = baseHP + equipmentHP;
			Stats.MaxMP = baseMP + equipmentMP;

			// 현재 HP/MP도 비례적으로 조정
			if(0 < oldMaxHP)
			{
				float hpRatio = (float)Stats.CurrentHP / oldMaxHP;
				Stats.CurrentHP = Math.Min( Stats.MaxHP, (int)(Stats.MaxHP * hpRatio));
			}

			if(0 < oldMaxMP)
			{
				float mpRatio = (float)Stats.CurrentMP / oldMaxMP;
				Stats.CurrentMP = Math.Min( Stats.MaxMP, (int)(Stats.MaxMP * mpRatio));
			}

			UpdateLastUpdateTime();
		}

		private bool ApplyItemEffect(InventoryItem item, int quantity)
		{
			// 아이템 ID에 따른 효과 적용(임시)
			return item.ItemId switch
			{
				1001 => UseHealthPotion( quantity * 50 ),			// 체력 포션 소
				1002 => UseHealthPotion( quantity * 100 ),          // 체력 포션 중
				1003 => UseManaPotion( quantity * 30 ),				// 마나 포션 소
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
			Stats.CurrentMP = Math.Min(MaxMP, CurrentMP + manaAmount);

			OnManaChanged.Invoke( this, oldMP, CurrentMP );
			UpdateLastUpdateTime();
			return true;
		}

		private bool UseFullHealthPotion()
		{
			if(MaxHP <= CurrentHP && MaxMP <= CurrentMP) return false;

			int oldHP = CurrentHP;
			int oldMP = CurrentMP;

			Stats.CurrentHP = MaxHP;
			Stats.CurrentMP = MaxMP;

			RaiseOnHealthChanged( oldHP, CurrentHP );
			OnManaChanged.Invoke(this, oldMP, CurrentMP );
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
				PlayerDetailInfo = new PlayerDetailInfo
				{
					IsGameMaster = false
				}
			};
		}
	}
}
