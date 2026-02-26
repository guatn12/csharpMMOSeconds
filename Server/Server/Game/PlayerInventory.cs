using Server.Database.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game
{
	/// <summary>
	/// 플레이어 인벤토리
	/// - 메모리 기반 인벤토리 관리
	/// - 더티 플래그를 통한 최적화된 redis/db 동기화
	/// - 스레드 안전 보장 (Player의 JobQueue 내에서만 실행)
	/// </summary>
	public class PlayerInventory
	{
		private readonly long _playerRawId;
		private readonly Dictionary<int, InventoryItem> _items;	// Slot -> Item 매핑
		private readonly HashSet<int> _dirtySlots;  // 변경된 슬롯 추적
		private long _gold = 0;
		private int _maxSlots = 50;
		private bool _isDirty = false;
		private DateTime _lastSorted = DateTime.UtcNow;

		// 이벤트 시스템 - 인벤토리 변경 알림
		public event Action<PlayerInventory, int, InventoryItem> OnItemAdded;
		public event Action<PlayerInventory, int, InventoryItem> OnItemRemoved;
		public event Action<PlayerInventory, int, int> OnItemQuantityChanged;
		public event Action<PlayerInventory, long, long> OnGoldChanged;
		public event Action<PlayerInventory> OnInventoryChanged;

		public PlayerInventory(long playerRawId, int maxSlots=50)
		{
			_playerRawId = playerRawId;
			_maxSlots = maxSlots;
			_items = new Dictionary<int, InventoryItem>();
			_dirtySlots = new HashSet<int>();
		}

		// 기본 속성
		public long PlayerRawId => _playerRawId;
		public long Gold => _gold;
		public int MaxSlots => _maxSlots;
		public int UsedSlots => _items.Count;
		public int FreeSlots => _maxSlots - UsedSlots;
		public bool IsDirty => _isDirty || _dirtySlots.Any();
		public DateTime LastSorted => _lastSorted;

		// 아이템 조회 메서드들
		public InventoryItem GetItem(int slot)
		{
			return _items.TryGetValue( slot, out var item ) ? item : null;
		}

		public List<InventoryItem> GetAllItems()
		{
			return _items.Values.ToList();
		}

		public List<InventoryItem> GetItemsByType(int itemId)
		{
			return _items.Values.Where(i => i.ItemId == itemId).ToList();
		}

		public int GetItemQuantity(int itemId)
		{
			return _items.Values
				.Where( i => i.ItemId == itemId )
				.Sum( i => i.Quantity );
		}

		public bool HasItem(int itemId, int quantity = 1)
		{
			return quantity <= GetItemQuantity( itemId );
		}

		public bool HasSpace(int requiredSlots = 1)
		{
			return requiredSlots <= FreeSlots;
		}

		// 아이템 추가/제거 메서드들
		public bool AddItem(int itemId, int quantity = 1, Dictionary<string, double> options = null)
		{
			if(itemId <= 0 || quantity <= 0) return false;

			// 스택 가능한 아이템인지 체크
			InventoryItem existingItem = _items.Values.FirstOrDefault(i => i.ItemId == itemId);
			if(existingItem != null)
			{
				// 기존 아이템 수량 증가
				return IncreaseItemQuantity( existingItem.Slot, quantity );
			}

			// 새 슬롯에 아이템 추가
			int emptySlot = FindEmptySlot();
			if(emptySlot == -1) return false;   // 인벤토리 가득 참

			var newItem = new InventoryItem
			{
				ItemId = itemId,
				Slot = emptySlot,
				Quantity = quantity,
				Options = options ?? new Dictionary<string, double>(),
				AcquiredAt = DateTime.UtcNow
			};

			_items[emptySlot] = newItem;
			MarkSlotDirty( emptySlot );

			// 이벤트 발생.
			OnItemAdded?.Invoke( this, emptySlot, newItem );
			OnInventoryChanged?.Invoke( this );

			return true;
		}

		public bool RemoveItem(int itemId, int quantity = 1)
		{
			if(itemId <= 0 || quantity <= 0) return false;
			if(!HasItem( itemId, quantity )) return false;

			int remainingToRemove = quantity;
			var itemsToRemove = new List<(int slot, InventoryItem item)>();

			// 제거해야 할 아이템 찾기
			foreach(var kvp in _items)
			{
				if(kvp.Value.ItemId == itemId)
				{
					// 계수가 0개 이하일 경우 예외...
					if(remainingToRemove <= 0) break;

					int removeFromThis = Math.Min(remainingToRemove, kvp.Value.Quantity);
					remainingToRemove -= removeFromThis;
					itemsToRemove.Add( (kvp.Key, kvp.Value) );
				}
			}

			// 실제 제거 수행
			foreach(var (slot, item) in itemsToRemove)
			{
				int removeQuantity = Math.Min(quantity, item.Quantity);
				if(item.Quantity <= removeQuantity)
				{
					// 아이템 완전 제거
					_items.Remove( slot );
					OnItemRemoved?.Invoke( this, slot, item );
				}
				else
				{
					// 수량만 감소
					item.Quantity -= removeQuantity;
					OnItemQuantityChanged?.Invoke( this, slot, item.Quantity );
				}

				MarkSlotDirty( slot );
				quantity -= removeQuantity;

				if(quantity <= 0) break;
			}

			OnInventoryChanged?.Invoke( this );
			return remainingToRemove == 0;
		}

		public bool RemoveItemFromSlot(int slot)
		{
			if(!_items.TryGetValue( slot, out var item )) return false;

			_items.Remove( slot );
			MarkSlotDirty( slot );

			OnItemRemoved?.Invoke( this, slot, item );
			OnInventoryChanged?.Invoke( this );

			return true;
		}

		public bool UseItem(int slot, int quantity = 1)
		{
			if (!_items.TryGetValue( slot, out var item )) return false;
			if(item.Quantity < quantity) return false;

			// 일단 수량 감소
			if(item.Quantity <= quantity)
			{
				// 아이템 완전 소모
				_items.Remove( slot );
				OnItemRemoved?.Invoke( this, slot, item );
			}
			else
			{
				// 수량만 감소
				item.Quantity -= quantity;
				OnItemQuantityChanged?.Invoke ( this, slot, item.Quantity );
			}

			MarkSlotDirty( slot );
			OnInventoryChanged?.Invoke( this );

			return true;
		}

		public bool AddGold(long amount)
		{
			if(amount <= 0) return false;

			long oldGold = _gold;
			_gold = Math.Min( long.MaxValue - 1000000, _gold + amount ); // 오버 플로우 방지.

			MarkDirty();
			OnGoldChanged?.Invoke( this, oldGold, _gold );
			OnInventoryChanged?.Invoke( this );

			return true;
		}

		public bool RemoveGold(long amount)
		{
			if(amount <= 0 || _gold < amount) return false;
			long oldGold = _gold;
			_gold -= amount;

			MarkDirty();
			OnGoldChanged?.Invoke(this, oldGold, _gold );
			OnInventoryChanged?.Invoke( this );

			return true;
		}

		// 인벤토리 관리 메서드들
		public bool MoveItem(int fromSlot , int toSlot)
		{
			if(fromSlot == toSlot) return false;
			if(!_items.TryGetValue( fromSlot, out var item )) return false;
			if(toSlot < 0 || _maxSlots < toSlot) return false;

			if(!_items.ContainsKey(toSlot))
			{
				_items.Remove( fromSlot );
				item.Slot = toSlot;
				_items[ toSlot ] = item;

				MarkSlotDirty( fromSlot );
				MarkSlotDirty( toSlot );
				OnInventoryChanged?.Invoke( this );
				return true;
			}

			// 목표 슬롯에 아이템이 있는 경우 - 교체
			InventoryItem targetItem = _items[toSlot];
			_items[ fromSlot ] = targetItem;
			targetItem.Slot = fromSlot;
			_items[ toSlot ] = item;
			item.Slot = toSlot;

			MarkSlotDirty( fromSlot );
			MarkSlotDirty( toSlot );
			OnInventoryChanged?.Invoke( this );
			return true;
		}

		public void SortInventory()
		{
			// 아이템을 ID 순서로 정렬하여 재배치
			var items = _items.Values.OrderBy(i => i.ItemId).ToList();
			_items.Clear();

			for(int i = 0; i < items.Count; i++)
			{
				items[ i ].Slot = i;
				_items[ i ] = items[ i ];
				MarkSlotDirty( i );
			}

			_lastSorted = DateTime.UtcNow;
			MarkDirty();
			OnInventoryChanged?.Invoke( this );
		}

		// 동기화 메서드들
		public InventoryModel ToInventoryModel()
		{
			return new InventoryModel
			{
				Items = _items.Values.ToList(),
				Gold = _gold,
				LastSorted = _lastSorted,
				ExtensionData = new Dictionary<string, object>
				{
					[ "maxSlots" ] = _maxSlots,
					[ "playerRawId" ] = _playerRawId
				}
			};
		}

		public void LoadFromInventoryModel(InventoryModel model)
		{
			if(model == null) return;

			_items.Clear();
			_dirtySlots.Clear();

			foreach(var item in model.Items)
			{
				if(0 <= item.Slot && item.Slot < _maxSlots)
				{
					_items[ item.Slot ] = item;	
				}
			}

			_gold= model.Gold;
			_lastSorted= model.LastSorted;

			// 확장 데이터에서 추가 정보 로드
			if (model.ExtensionData != null)
			{
				if(model.ExtensionData.TryGetValue("maxSlots", out var maxSlotsObj) && maxSlotsObj is int maxSlots)
				{
					_maxSlots = Math.Max( 10, Math.Min( maxSlots, 200 ) );
				}
			}

			_isDirty = false;
		}

		public void MarkClean()
		{
			_isDirty = false;
			_dirtySlots.Clear();
		}

		public HashSet<int> GetDirtySlots()
		{
			return new HashSet<int>(_dirtySlots);
		}

		// 유틸
		private int FindEmptySlot()
		{
			for(int i = 0; i < _maxSlots; i++)
			{
				if(!_items.ContainsKey( i ))
					return i;
			}

			return -1;	// 빈 슬롯 없음.
		}

		private bool IncreaseItemQuantity(int slot, int quantity)
		{
			if(!_items.TryGetValue(slot, out var item)) return false;

			int oldQuantity = item.Quantity;
			item.Quantity = Math.Min( int.MaxValue - 10000, item.Quantity + quantity );

			MarkSlotDirty( slot );
			OnItemQuantityChanged?.Invoke(this, slot, item.Quantity );
			OnInventoryChanged?.Invoke( this );

			return true;
		}

		private void MarkDirty()
		{
			_isDirty = true;
		}

		private void MarkSlotDirty(int slot)
		{
			_dirtySlots.Add( slot );
			_isDirty = true;
		}

		public bool IsValid()
		{
			foreach(var kvp in _items)
			{
				if(kvp.Key < 0 || _maxSlots <= kvp.Key) return false;
				if(kvp.Value.Slot != kvp.Key) return false;
				if(kvp.Value.ItemId <= 0 || kvp.Value.Quantity <= 0) return false;
			}

			if(_gold < 0) return false;

			return true;
		}

		public override string ToString()
		{
			return $"Inventory[Player:{_playerRawId}], Items:{UsedSlots}/{_maxSlots}, Gold: { _gold}, Dirty: { IsDirty}";			
		}

		public Dictionary<string, object> GetStatistics()
		{
			var stats = new Dictionary<string, object>
			{
				["playerRawId"] = _playerRawId,
				["usedSlots"] = UsedSlots,
				["maxSlots"] = _maxSlots,
				["freeSlots"] = FreeSlots,
				["gold"] = _gold,
				["totalItems"] = _items.Values.Sum(item => item.Quantity),
				["uniqueItems"] = _items.Values.Select(item => item.ItemId).Distinct().Count(),
				["isDirty"] = IsDirty,
				["dirtySlots"] = _dirtySlots.Count,
				["lastSorted"] = _lastSorted
			};

			return stats;
		}
	}
}
