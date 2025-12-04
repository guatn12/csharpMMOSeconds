using Protocol;
using Server.Database.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Extensions
{
	/// <summary>
	/// DB Entity -> Protobuf 메서드 변환 확장 메서드
	/// </summary>
	public static class EntityExtensions
	{
		public static InventoryItemInfo ToProto(this InventoryItem item)
		{
			if(item == null)
				throw new ArgumentNullException( nameof( item ) );

			var proto = new InventoryItemInfo
			{
				ItemId = item.ItemId,
				Quantity = item.Quantity,
				Slot = item.Slot,
				EnhancementLevel = item.Enhancement == null ? 0 : item.Enhancement.Level,
				CustomName = item.CustomName ?? string.Empty,
				AcquiredAt = item.AcquiredAt?.ToUnixTimeSeconds() ?? 0
			};

			if(item.Options != null)
			{
				foreach( var option in item.Options )
				{
					proto.Options.Add(option.Key, option.Value);
				}
			}

			return proto;
		}

		/// <summary>
		/// DateTime -> Unix timestamp (초)
		/// </summary>
		public static long ToUnixTimeSeconds(this DateTime datetime)
		{
			return new DateTimeOffset( datetime ).ToUnixTimeSeconds();
		}

		/// <summary>
		/// Unix tempstamp -> Datetime
		/// </summary>
		public static DateTime FromUnixTimeSecods(this long unixTime)
		{
			return DateTimeOffset.FromUnixTimeSeconds( unixTime ).DateTime;
		}
	}
}
