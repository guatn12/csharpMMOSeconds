using DatabaseLib.Entities;
using Microsoft.Extensions.Options;
using Protocol;
using Server.Data;
using Server.Data.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;

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
		private readonly Dictionary<long, InventoryItem> _instanceItems;		// InstanceId -> Item 매핑
		private readonly IDataManager _dataManager;

		private long _gold = 0;
		private int _maxSlots = 50;
		private DateTime _lastSorted = DateTime.UtcNow;
		private long _nextInstanceId = 1;

		// 이벤트 시스템 - 인벤토리 변경 알림
		//public event Action<PlayerInventory, long, InventoryItem> OnItemAdded;
		//public event Action<PlayerInventory, long, InventoryItem> OnItemRemoved;
		//public event Action<PlayerInventory, long, InventoryItem> OnItemQuantityChanged;
		public event Action<PlayerInventory, long, long> OnGoldChanged;
		public event Action<PlayerInventory, InventoryUpdateEventArgs> OnInventoryUpdated;

		public PlayerInventory( IDataManager dataManager, long playerRawId, int maxSlots=50)
		{
			_dataManager = dataManager;
			_playerRawId = playerRawId;
			_maxSlots = maxSlots;
			_instanceItems = new Dictionary<long, InventoryItem>();
		}

		// 기본 속성
		public long PlayerRawId => _playerRawId;
		public long Gold => _gold;
		public int MaxSlots => _maxSlots;
		public int UsedSlots => _instanceItems.Count;
		public int FreeSlots => _maxSlots - UsedSlots;
		public DateTime LastSorted => _lastSorted;

		// 아이템 조회 메서드들
		public List<InventoryItem> GetAllItems()
		{
			return _instanceItems.Values.ToList();
		}

		public List<InventoryItem> GetItemsByType(int itemId)
		{
			return _instanceItems.Values.Where(i => i.ItemId == itemId).ToList();
		}
		public InventoryItem GetItemByInstanceId( long instanceId )
		{
			return _instanceItems.TryGetValue( instanceId, out var item ) ? item : null;
		}

		public int GetItemQuantity(int itemId)
		{
			return _instanceItems.Values
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
		/// <summary>
		/// 내부에서 이벤트 처리를 하지 않고 스택만 쌓도록 처리되어 있음.
		/// </summary>
		//public bool AddItem( InventoryUpdateEventArgs eventArgs, int itemId, int quantity = 1, Dictionary<string, double> options = null)
		//{
		//	if(itemId <= 0 || quantity <= 0) 
		//		return false;

		//	// 스택 가능한 아이템인지 체크
		//	InventoryItem existingItem = _instanceItems.Values.FirstOrDefault(i => i.ItemId == itemId);
		//	var itemStaticData = _dataManager.GetItem(itemId);
		//	if(itemStaticData == null)
		//		return false;

		//	if( itemStaticData.IsStackable && existingItem != null && !existingItem.IsEquipped)
		//	{
		//		// 기존 아이템 수량 증가
		//		return IncreaseQuantityInternal( eventArgs, existingItem, quantity );
		//	}

		//	// 새 슬롯에 아이템 추가
		//	int emptySlot = FindEmptySlot();
		//	if(emptySlot == -1) 
		//		return false;   // 인벤토리 가득 참

		//	// 아이템 생성 및 InstanceId 생성
		//	var newItem = new InventoryItem
		//	{
		//		InstanceId = _nextInstanceId++,
		//		ItemId = itemId,
		//		Slot = emptySlot,
		//		Quantity = quantity,
		//		Options = options ?? new Dictionary<string, double>(),
		//		AcquiredAt = DateTime.UtcNow
		//	};

		//	_instanceItems[newItem.InstanceId] = newItem;
		//	MarkInstanceDirty( newItem.InstanceId );

		//	return true;
		//}

		/// <summary>
		/// 다수의 아이템 일괄 처리
		/// </summary>
		/// <returns></returns>
		public bool AddItems( IEnumerable<InventoryItemInfo> items )
		{
			var itemList = items?.ToList();
			if(itemList == null || itemList.Count == 0)
				return false;

			var eventArgs = new InventoryUpdateEventArgs();
			var newItems = new List<InventoryItem>();
			var stackItems = new Dictionary<int, (InventoryItem, int)>();
			var bookSlots = new List<int>();
			long calcNextInstanceId = _nextInstanceId;
			// 리스트에서 문제가 없는지 체크
			foreach(var item in itemList)
			{
				if(item.Quantity <= 0)
					return false;

				InventoryItem existingItem = _instanceItems.Values.FirstOrDefault(i => i.ItemId == item.ItemId);
				var itemStaticData = _dataManager.GetItem(item.ItemId);
				if(itemStaticData == null)
					return false;

				// 해당 아이템이 스택형일 경우 스택 리스트에 추가.
				if(itemStaticData.IsStackable && existingItem != null && !existingItem.IsEquipped)
				{
					if(stackItems.TryGetValue( item.ItemId, out var stackItem ))
					{
						long quantity = stackItems[ item.ItemId ].Item2 + item.Quantity;
						if(int.MaxValue - existingItem.Quantity < quantity)
							return false;
						stackItems[ item.ItemId ] = (existingItem, (int)quantity);
					}
					else
						stackItems.Add( item.ItemId, (existingItem, item.Quantity) );

					// 스택 가능 수량 체크
					if(!IsIncreaseQuantityInternal( existingItem, stackItem.Item2 == 0 ? item.Quantity : stackItem.Item2 + item.Quantity ))
						return false;

					continue;
				}

				// 새 슬롯에 아이템 추가
				int emptySlot = FindEmptySlot(bookSlots);
				if(emptySlot == -1)
					return false;   // 인벤토리 가득 참

				newItems.Add( new InventoryItem
				{
					InstanceId = calcNextInstanceId++,
					ItemId = item.ItemId,
					Slot = emptySlot,
					Quantity = item.Quantity,
					Options = item.Options.ToDictionary(),
					AcquiredAt = DateTime.UtcNow
				} );
				bookSlots.Add( emptySlot );
			}

			// 스택형 아이템 추가
			foreach(var item in stackItems.Values)
			{
				IncreaseQuantityInternal( eventArgs, item.Item1, item.Item2 );
			}

			// 신규 아이템 추가.
			foreach(var item in newItems)
			{
				_instanceItems[ item.InstanceId ] = item;
				eventArgs.ChangedItems.Add( item );
			}
			_nextInstanceId = calcNextInstanceId;

			// 이벤트 발생.
			if(eventArgs.HasChanges)
				OnInventoryUpdated?.Invoke( this, eventArgs );

			return true;
		}

		public bool RemoveItems( IEnumerable<long> instanceIds )
		{
			var ids = instanceIds?.Distinct().ToList();
			if(ids == null || ids.Count == 0)
				return false;

			var eventArgs = new InventoryUpdateEventArgs();
			var removeItems = new List<InventoryItem>();
			// 사전 검증
			foreach(var id in ids)
			{
				// 소지 여부 확인
				if(!_instanceItems.TryGetValue( id, out var item ))
					return false;

				// 장착중
				if(item.IsEquipped)
					return false;

				removeItems.Add( item );
			}

			foreach(var item in removeItems)
			{
				if(RemoveInstanceInternal( eventArgs, item ) == false)
					return false;
			}

			if(eventArgs.HasChanges)
				OnInventoryUpdated?.Invoke( this, eventArgs );

			return true;
		}

		public bool ConsumeItemsByItemId( int itemId, int quantity = 1)
		{
			var eventArgs = new InventoryUpdateEventArgs();

			if(itemId <= 0 || quantity <= 0) 
				return false;

			int remaining = quantity;
			var removeItems = new List<(InventoryItem item, int removeQunetity)>();
			foreach(var item in _instanceItems.Values)
			{
				if(item.ItemId != itemId || item.IsEquipped)
					continue;

				int removeQunetity = Math.Min(remaining, item.Quantity);
				removeItems.Add( (item, removeQunetity) );
				remaining -= removeQunetity;
				if(remaining == 0)
					break;
			}

			if(0 < remaining)
				return false;

			foreach(var (item, removeQunetity) in removeItems)
			{
				DecreaseQuantityInternal( eventArgs, item, removeQunetity );
			}

			if(eventArgs.HasChanges)
				OnInventoryUpdated?.Invoke( this, eventArgs );
			return true;
		}

		public bool UseItem(long instanceId, int quantity = 1)
		{
			var eventArgs = new InventoryUpdateEventArgs();

			var item = GetItemByInstanceId(instanceId);
			if(item == null)
				return false;

			if(item.Quantity < quantity)
				return false;

			bool success = DecreaseQuantityInternal( eventArgs, item, quantity );
			if(success)
			{
				if(eventArgs.HasChanges)
					OnInventoryUpdated?.Invoke( this, eventArgs );
			}

			return success;
		}

		public bool AddGold(long amount)
		{
			var eventArgs = new InventoryUpdateEventArgs();

			if(amount <= 0) 
				return false;

			long addableAmount = Math.Min(amount, (long.MaxValue - 1000000) - _gold); // 오버 플로우 방지.
			if(addableAmount <= 0)
				return false;

			long oldGold = _gold;
			_gold += addableAmount;  

			eventArgs.NewGold = _gold;

			if(eventArgs.HasChanges)
			{
				OnInventoryUpdated?.Invoke( this, eventArgs );
				OnGoldChanged?.Invoke( this, oldGold, _gold );
			}
				

			return true;
		}

		public bool RemoveGold(long amount)
		{
			var eventArgs = new InventoryUpdateEventArgs();

			if(amount <= 0 || _gold < amount) return false;
			long oldGold = _gold;
			_gold -= amount;

			eventArgs.NewGold = _gold;

			if(eventArgs.HasChanges)
			{
				OnInventoryUpdated?.Invoke( this, eventArgs );
				OnGoldChanged?.Invoke( this, oldGold, _gold );
			}
				

			return true;
		}

		// 인벤토리 관리 메서드들
		public bool MoveItem(int fromSlot , int toSlot)
		{
			var eventArgs = new InventoryUpdateEventArgs();

			if(fromSlot == toSlot) 
				return false;

			var fromItem = _instanceItems.Values.Where(i => i.Slot == fromSlot).FirstOrDefault();
			var toItem = _instanceItems.Values.Where(i => i.Slot == toSlot).FirstOrDefault();

			if(fromItem == null) 
				return false;

			if(toItem == null && (toSlot < 0 || _maxSlots <= toSlot))
				return false;

			// 목적지에 아이템 존재 여부
			if(toItem == null)
			{
				fromItem.Slot = toSlot;
				eventArgs.ChangedItems.Add( fromItem );
				OnInventoryUpdated?.Invoke( this, eventArgs );
				return true;
			}
			else
			{
				fromItem.Slot = toSlot;
				toItem.Slot = fromSlot;
				eventArgs.ChangedItems.Add( fromItem );
				eventArgs.ChangedItems.Add( toItem );
				OnInventoryUpdated?.Invoke( this, eventArgs );
				return true;
			}
		}

		public void SortInventory()
		{
			var eventArgs = new InventoryUpdateEventArgs();
			// 아이템을 instanceId 순서로 정렬하여 재배치
			var items = _instanceItems.Values.OrderBy(i => i.InstanceId).ToList();
			_instanceItems.Clear();

			for(int i = 0; i < items.Count; i++)
			{
				items[ i ].Slot = i;
				_instanceItems[ items[i].InstanceId ] = items[ i ];
			}

			eventArgs.ChangedItems.AddRange( _instanceItems.Values );

			_lastSorted = DateTime.UtcNow;
			if(eventArgs.HasChanges)
				OnInventoryUpdated?.Invoke( this, eventArgs );
		}

		// 동기화 메서드들
		public InventoryModel ToInventoryModel()
		{
			return new InventoryModel
			{
				Items = _instanceItems.Values.ToList(),
				Gold = _gold,
				LastSorted = _lastSorted,
				NextInstanceId = _nextInstanceId,
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

			_instanceItems.Clear();

			// 확장 데이터에서 추가 정보 로드
			if(model.ExtensionData != null)
			{
				if(model.ExtensionData.TryGetValue( "maxSlots", out var maxSlotsObj ) && maxSlotsObj is int maxSlots)
				{
					_maxSlots = Math.Max( 10, Math.Min( maxSlots, 200 ) );
				}
			}

			foreach(var item in model.Items)
			{
				if(0 <= item.Slot && item.Slot < _maxSlots)
				{
					_instanceItems[ item.InstanceId ] = item;	
				}
			}

			_gold= model.Gold;
			_lastSorted= model.LastSorted;
			_nextInstanceId = model.NextInstanceId;

		}

		private bool RemoveInstanceInternal( InventoryUpdateEventArgs eventArgs, InventoryItem item )
		{
			if(item.IsEquipped)
				return false;

			if(_instanceItems.Remove( item.InstanceId ) == false)
				return false;

			eventArgs.RemovedItemInstanceIds.Add( item.InstanceId );

			return true;
		}

		private bool DecreaseQuantityInternal( InventoryUpdateEventArgs eventArgs, InventoryItem item, int quantity )
		{
			if(quantity <= 0 || item.Quantity < quantity) 
				return false;
			
			if(item.Quantity == quantity) 
				return RemoveInstanceInternal( eventArgs, item );

			item.Quantity -= quantity;
			eventArgs.ChangedItems.Add( item );

			return true;
		}

		private bool IsIncreaseQuantityInternal(InventoryItem item, int quantity)
		{
			int remainingCapa = int.MaxValue - item.Quantity; // 오버 플로우 방지.
			if(quantity <= 0 || remainingCapa < quantity)
				return false;

			return true;
		}

		private bool IncreaseQuantityInternal( InventoryUpdateEventArgs eventArgs, InventoryItem item, int quantity )
		{
			int remainingCapa = int.MaxValue - item.Quantity; // 오버 플로우 방지.
			if(quantity <= 0 || remainingCapa < quantity)
				return false;

			int oldQuantity = item.Quantity;
			item.Quantity += quantity;

			eventArgs.ChangedItems.Add( item );

			return true;
		}

		// 유틸
		private int FindEmptySlot(List<int> bookSlots = null)
		{
			HashSet<int> slotSet = _instanceItems.Values.Select(x => x.Slot).ToHashSet();
			if(bookSlots != null)
			{
				foreach(int slot in bookSlots)
					slotSet.Add( slot );
			}

			for(int i = 0; i < _maxSlots; i++)
			{
				if(!slotSet.Contains( i ))
					return i;
			}

			return -1;	// 빈 슬롯 없음.
		}

		public bool IsValid()
		{
			foreach(var kvp in _instanceItems)
			{
				if(kvp.Value.Slot < 0 || _maxSlots <= kvp.Value.Slot) 
					return false;

				if(kvp.Value.ItemId <= 0 || kvp.Value.Quantity <= 0) 
					return false;
			}

			if(_gold < 0) 
				return false;

			if(_nextInstanceId < 0)
				return false;

			return true;
		}

		public override string ToString()
		{
			return $"Inventory[Player:{_playerRawId}], Items:{UsedSlots}/{_maxSlots}, Gold: { _gold}";			
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
				["totalItems"] = _instanceItems.Values.Sum(item => item.Quantity),
				["uniqueItems"] = _instanceItems.Values.Select(item => item.ItemId).Distinct().Count(),
				["lastSorted"] = _lastSorted
			};

			return stats;
		}
	}
}
