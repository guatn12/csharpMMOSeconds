using Protocol;
using DatabaseLib.Entities;
using Server.Game.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using Server.Data;
using Server.Services.Persistence;
using System.Text.Json;

namespace Server.Game
{
	public class Player : GameObject
	{
		// 플레이어 데이터
		private long _combatTargetId = 0;
		private readonly Dictionary<int, DateTime> _skillCooldowns = new Dictionary<int, DateTime>();
		private readonly PlayerPersistenceState _persistence = new PlayerPersistenceState();

		// 공격 쿨다운 (이동 제한용)
		private DateTime _lastAttackTime = DateTime.MinValue;
		private readonly TimeSpan _attackCooldown = TimeSpan.FromSeconds(1); // 공격 후 1초간 이동 불가

		public long TotalPlayTimeMinutes { get; private set; }
		public DateTime? LastEnteredGameAt { get; private set; }

		public PlayerSettingsModel PlayerSettings { get; private set; } = new();

		// 인벤, 장비
		public PlayerInventory Inventory { get; private set; }
		public PlayerEquipment Equipment { get; private set; }

		// 이벤트 시스템
		public event Action<Player> OnLevelUp;
		public event Action<Player, int, int> OnManaChanged;   // (Player, oldMP, newMP)

		// 인벤, 장비 관련 이벤트
		public event Action<Player> OnEquipmentChanged;                                                             // 장비 변경
		public event Action<Player, InventoryUpdateEventArgs> OnInventoryUpdated;                                   // 인벤토리 변경
		public event Action<Player, long, long> OnGoldChanged;                                                      // 골드 변경

		public Player( IDataManager dataManager, long playerRawId, string playerName )
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

			Inventory = new PlayerInventory( dataManager, playerRawId );
			Equipment = new PlayerEquipment( playerRawId );

			// 이벤트 구독 설정
			SetupCompositionEvents();
		}

		public long Experience => Stats.Experience;
		public float HPPercentage => 0 < Stats.MaxHP ? (float)Stats.CurrentHP / MaxHP : 0f;
		public float MPPercentage => 0 < Stats.MaxMP ? (float)Stats.CurrentMP / MaxMP : 0f;
		public long RequiredExp => Stats.Level * 100; // 임시 레벨업 필요 경험치.
		public long CombatTargetId => _combatTargetId;

		public void RecordInitialGameEntry(DateTime enteredAtUtc)
		{
			if(enteredAtUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException( "enteredAtUtc must be in UTC.", nameof( enteredAtUtc ) );

			LastEnteredGameAt = enteredAtUtc;
			MarkPersistenceDirty();
		}


		public List<long> ApplyLoadedData(PlayerEntity playerEntity, PlayerStateEntity playerStateEntity, InventoryEntity inventoryEntity, EquipmentEntity equipmentEntity)
		{
			var missingEquipInstances = new List<long>();
			_name = playerEntity.PlayerName;
			_statInfo.Level = playerEntity.Level;
			_statInfo.Experience = playerEntity.Experience;
			TotalPlayTimeMinutes = playerEntity.TotalPlayTimeMinutes;
			PlayerSettings = playerEntity.PlayerSettings;
			LastEnteredGameAt = playerEntity.LastEnteredGameAt;

			// 장비 데이터보다 무조건 우선
			if(inventoryEntity?.InventoryData != null)
			{
				LoadInventoryData( inventoryEntity.InventoryData );
			}

			if(equipmentEntity?.EquipmentData != null)
			{
				var equipmentDict = equipmentEntity.EquipmentData.ToDictionary(kv => (PlayerEquipment.EquipSlot)kv.Key, kv => kv.Value);
				missingEquipInstances = LoadEquipmentFromRefs( equipmentDict );
			}
			else
			{
				RecalculatePlayerStats();
			}

			_persistence.ResetAfterLoad( playerEntity.AccountId, inventoryEntity.InventoryId, inventoryEntity.Version,
				equipmentEntity.EquipmentId, equipmentEntity.Version, playerStateEntity.PlayerStateId, playerStateEntity.Version );

			return missingEquipInstances;
		}

		public void ApplyLoadedVitals( int currentHp, int currentMp )
		{
			Stats.CurrentHP = currentHp <= 0 ? Stats.MaxHP : currentHp;
			Stats.CurrentMP = currentMp <= 0 ? Stats.MaxMP : currentMp;

			SetState( State.Idle );
		}

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
			MarkPersistenceDirty();
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
			MarkPersistenceDirty();
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
			MarkPersistenceDirty();
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
				OnLevelUp?.Invoke( this );

				// HP/MP 변경 이벤트 발생
				RaiseOnHealthChanged( oldHP, CurrentHP );
				OnManaChanged?.Invoke(this, oldMP, CurrentMP );
			}

			MarkPersistenceDirty();
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
			MarkPersistenceDirty();
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
			OnManaChanged?.Invoke( this, oldMP, CurrentMP );

			UpdateLastUpdateTime();
			MarkPersistenceDirty();
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

		public bool UseItem(long instanceId, int quantity = 1)
		{
			InventoryItem item = Inventory.GetItemByInstanceId( instanceId );
			if(item == null) 
				return false;

			if(quantity <= 0 || item.Quantity < quantity || item.IsEquipped)
				return false;

			// 아이템 사용 효과 적용
			bool effectApplied = ApplyItemEffect(item, quantity);
			if(!effectApplied) 
				return false;

			// 인벤토리에서 아이템 소모
			return Inventory.UseItem(instanceId, quantity);
		}

		// 장비 관련
		public bool EquipItemFromInventory(long instanceId, int equipSlot)
		{
			InventoryItem item = Inventory.GetItemByInstanceId(instanceId);
			if(item == null) 
				return false;

			if(Equipment.GetEquipmentDict().Values.Contains( instanceId ))
				return false;

			if(!Equipment.CanEquipItem( item.ItemId )) 
				return false;

			// 새 장비 착용
			return Equipment.EquipItem( item );
		}

		public bool UnequipItemToInventory(PlayerEquipment.EquipSlot slot)
		{
			InventoryItem item = Equipment.GetEquippedItem(slot);
			if(item == null) 
				return false;
			
			// 장비 해제
			var unequippedItem = Equipment.UnequipItemAndReturn(slot);
			if(unequippedItem == null) 
				return false;

			return true;
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
				MaxHP = _statInfo.MaxHP,
				MaxMP = _statInfo.MaxMP,
			};
		}

		// 데이터 동기화 관련
		public PlayerPersistenceState Persistence => _persistence;
		public bool HasDirtyData() => _persistence.IsDirty;
		public void MarkPersistenceDirty() => _persistence.MarkDirty();

		public PlayerSaveSnapshot CreatePersistenceSnapshot( int mapId )
		{
			var state = new PlayerStateModel
			{
				MapId = mapId,
				PosX = PosInfo.PosX,
				PosY = PosInfo.PosY,
				PosZ = PosInfo.PosZ,
				RotationX = PosInfo.RotationX,
				RotationY = PosInfo.RotationY,
				RotationZ = PosInfo.RotationZ,
				CurrentHp = CurrentHP,
				CurrentMp = CurrentMP,
				CreatureState = (int)CreatureState,
			};

			return new PlayerSaveSnapshot( ObjectRawId, _persistence.AccountId, Persistence.DirtyRevision, Name, Level, Experience,
				TotalPlayTimeMinutes, JsonSerializer.Serialize( PlayerSettings ), _persistence.InventoryId, _persistence.InventoryDbVersion,
				Inventory.MaxSlots, JsonSerializer.Serialize( Inventory.ToInventoryModel() ), _persistence.EquipmentId, _persistence.EquipmentDbVersion,
				JsonSerializer.Serialize( Equipment.GetEquipmentRefs() ), _persistence.PlayerStateId, _persistence.PlayerStateDbVersion, JsonSerializer.Serialize( state ), LastEnteredGameAt );
		}

		// 인벤 / 장비 데이터 로드 (DB / Redis에서 복원용)
		public void LoadInventoryData(InventoryModel inventoryModel)
		{
			Inventory.LoadFromInventoryModel(inventoryModel);
		}

		public List<long> LoadEquipmentFromRefs(Dictionary<PlayerEquipment.EquipSlot, long> equipmentData)
		{
			List<long> missingEquipInstanceIds = new List<long>();
			Dictionary<PlayerEquipment.EquipSlot, InventoryItem> equipInfoDict = new Dictionary<PlayerEquipment.EquipSlot, InventoryItem>();
			var inventoryItems = Inventory.GetAllItems();
			foreach(var equip in equipmentData)
			{
				var item = inventoryItems.Where( item => item.InstanceId == equip.Value ).FirstOrDefault();
				if(item == null)
				{
					missingEquipInstanceIds.Add( equip.Value );
					continue;
				}

				equipInfoDict[ equip.Key ] = item;
				item.IsEquipped = true;
			}

			Equipment.LoadFromEquipmentData(equipInfoDict);
			RecalculatePlayerStats();

			return missingEquipInstanceIds;
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
			Inventory.OnInventoryUpdated += ( inv, eventArgs ) =>
			{
				MarkPersistenceDirty();
				OnInventoryUpdated?.Invoke( this, eventArgs );
			};
			Inventory.OnGoldChanged += ( inv, oldGold, newGold ) =>
			{
				// TODO : 일단 차단 - Inventory 내부에서 골드 변경시 OnInventoryUpdate / OnGoldChanged 둘다 발생시키고 있어 중복된다.
				//MarkPersistenceDirty();
				OnGoldChanged?.Invoke( this, oldGold, newGold );
			};

			// 장비 이벤트 구독
			Equipment.OnEquipmentChanged += ( eq ) =>
			{
				RecalculatePlayerStats();
				MarkPersistenceDirty();
				OnEquipmentChanged?.Invoke( this );
			};
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

			OnManaChanged?.Invoke( this, oldMP, CurrentMP );
			UpdateLastUpdateTime();
			MarkPersistenceDirty();
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
			OnManaChanged?.Invoke(this, oldMP, CurrentMP );
			UpdateLastUpdateTime();
			MarkPersistenceDirty();
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
