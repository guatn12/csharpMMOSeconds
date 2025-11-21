using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.DTOs
{
	/// <summary>
	/// 몬스터 처치 보상 정보를 담는 DTO
	/// </summary>
	public class RewardInfo
	{
		public int Experience {  get; set; }
		public int Gold { get; set; }
		public List<InventoryItemInfo> DroppedItem { get; set; } = new List<InventoryItemInfo>();
		public bool LeveledUp { get; set; }
		public int NewLevel { get; set; }
		public int NewMaxHP { get; set; }
		public int NewMaxMP { get; set; }
	}
}
