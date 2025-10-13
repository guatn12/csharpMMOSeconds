using Protocol;
using Server.Database.Entities;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game
{
	/// <summary>
	/// 플레이어 장비 클래스
	/// - 장비 착용 / 해제
	/// - 장비 스탯 적용
	/// - 장비 세트 효과 등
	/// </summary>
	public class PlayerEquipment
	{
		private readonly long _playerId;
		private readonly Dictionary<EquipSlot, InventoryItem> _equippedItems;   // 착용 중인 장비
		private readonly Dictionary<StatType, int> _equipmentStats; // 장비로 얻은 스탯
		private bool _isDirty = false;

		// 장비 슬롯 정의
		public enum EquipSlot
		{
			None = 0,
			Weapon = 1,			// 무기
			Shield = 2,			// 방패
			Helmet = 3,			// 투구
			Armor = 4,			// 갑옷
			Gloves = 5,			// 장갑
			Boots = 6,			// 신발
			Ring1 = 7,			// 반지1
			Ring2 = 8,			// 반지2
			Necklace = 9,		// 목걸이
			Earring = 10,		// 귀걸이
		}

		// 스탯 타입 정의
		public enum StatType
		{
			Attack = 1,				// 공격력
			Defense = 2,			// 방어력
			HP = 3,					// 체력
			MP= 4,					// 마나
			CriticalRate = 5,		// 치명타율
			Speed = 6,				// 이동속도
		}

		// 이벤트
		public event Action<PlayerEquipment, EquipSlot, InventoryItem> OnItemEquipped;			// 장착
		public event Action<PlayerEquipment, EquipSlot, InventoryItem> OnItemUnEquipped;        // 해제
		public event Action<PlayerEquipment, Dictionary<StatType, int>> OnStatsChanged;			// 스탯 변경
		public event Action<PlayerEquipment> OnEquipmentChanged;								// 전체 변경

		public PlayerEquipment(long playerId)
		{
			_playerId = playerId;
			_equippedItems = new Dictionary<EquipSlot, InventoryItem>();
			_equipmentStats = new Dictionary<StatType, int>();

			// 모든 스탯을 0으로 초기화
			foreach(StatType statType in Enum.GetValues<StatType>())
			{
				_equipmentStats[statType] = 0;
			}
		}

		// 기본 속성
		public long PlayerId => _playerId;
		public bool IsDirty => _isDirty;
		public Dictionary<StatType, int> EquipmentStats => new Dictionary<StatType, int>( _equipmentStats );
		public int EquippedItemCount => _equippedItems.Count;

		// 장비 조회 메서드들
		public InventoryItem GetEquippedItem(EquipSlot slot)
		{
			return _equippedItems.TryGetValue( slot, out var item ) ? item : null;
		}

		public List<InventoryItem> GetAllEquippedItems()
		{
			return _equippedItems.Values.ToList();
		}

		public bool IsSlotEquipped(EquipSlot slot)
		{
			return _equippedItems.ContainsKey( slot );
		}

		public bool HasWeaponEquipped()
		{
			return IsSlotEquipped(EquipSlot.Weapon);
		}

		public int GetTotalStat(StatType statType)
		{
			return _equipmentStats.TryGetValue( statType, out var Value ) ? Value : 0;
		}

		public int GetTotalAttack() => GetTotalStat(StatType.Attack);
		public int GetTotalDefense() => GetTotalStat(StatType.Defense);
		public int GetTotalHP() => GetTotalStat(StatType.HP);
		public int GetTotalMP() => GetTotalStat(StatType.MP);
		public float GetCriticalRate() => GetTotalStat( StatType.CriticalRate ) / 100.0f; // 백분율
		public int GetSpeed() => GetTotalStat(StatType.Speed);

		// 장비 착용 메서드들
		public bool EquipItem( InventoryItem item )
		{
			if(item == null) return false;

			// 아이템이 장비 가능한지 체크
			EquipSlot targetSlot = GetItemEquipSlot(item.ItemId);
			if(targetSlot == EquipSlot.None) return false;

			// 기존 장비가 있다면 해제
			if(_equippedItems.TryGetValue( targetSlot, out var existingItem ))
			{
				UnequipItem( targetSlot );
			}

			// 새 장비 착용
			_equippedItems[ targetSlot ] = item;
			ApplyItemStats( item, true );   // 스탯 적용.

			MarkDirty();

			// 이벤트 발생.
			OnItemEquipped?.Invoke( this, targetSlot, item );
			OnEquipmentChanged?.Invoke( this );

			return true;
		}

		public bool UnequipItem(EquipSlot slot)
		{
			if(!_equippedItems.TryGetValue(slot, out var item)) return false;

			// 장비 해제
			_equippedItems.Remove( slot );
			ApplyItemStats( item, false );

			MarkDirty();

			// 이벤트 발생.
			OnItemUnEquipped?.Invoke(this, slot, item );
			OnEquipmentChanged?.Invoke( this );

			return true;
		}

		public InventoryItem UnequipItemAndReturn(EquipSlot slot)
		{
			if(!_equippedItems.TryGetValue(slot, out var item)) return null;

			UnequipItem( slot );
			return item;
		}

		// 스탯 적용
		private void ApplyItemStats(InventoryItem item, bool apply)
		{
			if(item?.Options == null) return;

			var oldStats = new Dictionary<StatType, int>(_equipmentStats);
			int multiplier = apply ? 1 : -1; // 적용시 +1, 해제시 -1

			foreach(var option in item.Options)
			{
				StatType statType = ParseStatType(option.Key);
				if(statType != StatType.Attack) // 잘못된 스탯이면 스킵
				{
					int statValue = (int)option.Value * multiplier;
					_equipmentStats[ statType ] = Math.Max( 0, _equipmentStats[ statType ] + statValue );
				}
			}

			// 강화 보너스 적용
			if(item.Enhancement != null && 0 < item.Enhancement.Level )
			{
				ApplyEnhancementBonus( item, apply );
			}

			// 스탯이 변경되었으면 이벤트 발생
			if(!AreDictionariesEqual(oldStats, _equipmentStats))
			{
				OnStatsChanged?.Invoke(this, new Dictionary<StatType, int>(_equipmentStats));
			}
		}

		private void ApplyEnhancementBonus(InventoryItem item, bool apply)
		{
			if(item.Enhancement == null) return;

			int enhanceLevel = item.Enhancement.Level;
			int multiplier = apply ? 1 : -1;

			// 강화 레벨당 기본 스탯 5% 증가
			foreach(var option in item.Options)
			{
				StatType statType = ParseStatType(option.Key);
				if(statType != StatType.Attack)
				{
					int baseValue = (int)option.Value;
					int bonusValue = (int)(baseValue * 0.05f * enhanceLevel) * multiplier;

					_equipmentStats[statType] = Math.Max(0, _equipmentStats[statType] + bonusValue);
				}
			}
		}

		private StatType ParseStatType( string statName )
		{
			return statName.ToLower() switch
			{
				"attack" or "공격력" => StatType.Attack,
				"defense" or "방어력" => StatType.Defense,
				"hp" or "체력" => StatType.HP,
				"mp" or "마나" => StatType.MP,
				"critical" or "치명타" => StatType.CriticalRate,
				"speed" or "이동속도" => StatType.Speed,
				_ => StatType.Attack
			};
		}

		// 아이템 타입 판별 (임시 - 추후 정적 데이터로 대체)
		private EquipSlot GetItemEquipSlot( int itemId )
		{
			// ItemId 범위로 장비 슬롯 판별 (임시)
			return itemId switch
			{
			>= 1000 and < 2000 => EquipSlot.Weapon,    // 무기: 1000~1999
			>= 2000 and < 3000 => EquipSlot.Shield,    // 방패: 2000~2999
			>= 3000 and < 4000 => EquipSlot.Helmet,    // 투구: 3000~3999
			>= 4000 and < 5000 => EquipSlot.Armor,     // 갑옷: 4000~4999
			>= 5000 and < 6000 => EquipSlot.Gloves,    // 장갑: 5000~5999
			>= 6000 and < 7000 => EquipSlot.Boots,     // 부츠: 6000~6999
			>= 7000 and < 8000 => EquipSlot.Ring1,     // 반지: 7000~7999 (Ring 우선)
			>= 8000 and < 9000 => EquipSlot.Necklace,  // 목걸이: 8000~8999
			>= 9000 and < 10000 => EquipSlot.Earring,  // 귀걸이: 9000~9999
            _ => EquipSlot.None
			  };
		}

		public bool CanEquipItem(int itemId )
		{
			return GetItemEquipSlot( itemId ) != EquipSlot.None;
		}

		public bool IsRingItem(int itemId)
		{
			// TODO : 임시 수정 필요 정적 데이터로
			return 7000 <= itemId && itemId < 8000;
		}

		private EquipSlot GetAvailableRingSlot()
		{
			if(!IsSlotEquipped( EquipSlot.Ring1 )) return EquipSlot.Ring1;
			if(!IsSlotEquipped(EquipSlot.Ring2 )) return EquipSlot.Ring2;
			return EquipSlot.None; // 반지 슬롯 모두 사용중.
		}

		// 특수 장비 처리(반지 등)
		public bool EquipRing(InventoryItem ringItem)
		{
			if(!IsRingItem(ringItem.ItemId)) return false;

			EquipSlot availableSlot = GetAvailableRingSlot();
			if(availableSlot == EquipSlot.None)
			{
				// 두 슬롯 모두 차있으면 Ring1에 교체
				availableSlot = EquipSlot.Ring1;
			}

			if(_equippedItems.ContainsKey( availableSlot ))
			{
				UnequipItem( availableSlot );
			}

			_equippedItems[ availableSlot ] = ringItem;
			ApplyItemStats( ringItem, true );

			MarkDirty();
			OnItemEquipped?.Invoke( this, availableSlot, ringItem );
			OnEquipmentChanged?.Invoke( this );

			return true;
		}

		// 모든 장비 해제
		public List<InventoryItem> UnequipAllItems()
		{
			var unequippedItems = new List<InventoryItem>();

			foreach(var slot in _equippedItems.Keys.ToList())
			{
				var item = UnequipItemAndReturn(slot);
				if(item != null)
				{
					unequippedItems.Add( item );
				}
			}

			return unequippedItems;
		}

		// 데이터 동기화
		public Dictionary<EquipSlot, InventoryItem> ToEquipmentDictionary()
		{
			return new Dictionary<EquipSlot, InventoryItem>( _equippedItems );
		}

		public void LoadFromEquipmentData(Dictionary<EquipSlot, InventoryItem> equipmentData)
		{
			if(equipmentData == null) return;

			_equippedItems.Clear();
			ResetAllStats();

			foreach(var kvp in equipmentData)
			{
				_equippedItems[kvp.Key] = kvp.Value;
				ApplyItemStats(kvp.Value, true );
			}

			_isDirty = false;
		}

		public void MarkClean()
		{
			_isDirty = false;
		}

		private void MarkDirty()
		{
			_isDirty = true;
		}

		private void ResetAllStats()
		{
			foreach(StatType statType in Enum.GetValues<StatType>())
			{
				_equipmentStats[ statType ] = 0;
			}
		}

		// 유틸
		private bool AreDictionariesEqual<TKey, TValue>(Dictionary<TKey, TValue> dict1, Dictionary<TKey, TValue> dict2)
		{
			if(dict1.Count != dict2.Count) return false;

			foreach(var kvp in dict1)
			{
				if(!dict2.TryGetValue(kvp.Key, out var value) || !Equals(kvp.Value, value)) return false;
			}

			return true;
		}

		// 디버깅 및 검증
		public bool IsValid()
		{
			foreach(var kvp in _equippedItems)
			{
				if(kvp.Value == null) return false;
				if(GetItemEquipSlot( kvp.Value.ItemId ) != kvp.Key) return false;
			}

			foreach(var statValue in _equipmentStats.Values)
			{
				if(statValue < 0) return false;
			}

			return true;
		}

		public override string ToString()
		{
			return $"Equipment[Player:{_playerId}] Items:{EquippedItemCount} " +
				$"ATK:{GetTotalAttack()} DEF:{GetTotalDefense()} HP:{GetTotalHP()} Dirty:{IsDirty}";
		  }

		// 장비 세트 효과 (추후 확장용)
		public Dictionary<string, int> GetSetEffects()
		{
			var setEffects = new Dictionary<string, int>();
			// TODO: 장비 세트 효과 계산 로직 추가
			return setEffects;
		}
	}
}
