using DatabaseLib.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game
{
	public sealed class InventoryUpdateEventArgs : EventArgs
	{
		public List<InventoryItem> ChangedItems { get; } = new();
		public List<long> RemovedItemInstanceIds { get; } = new();
		public long? NewGold { get; set; }

		public bool HasChanges =>
			 0 < ChangedItems.Count ||
			0 < RemovedItemInstanceIds.Count ||
			NewGold.HasValue;
	}
}
