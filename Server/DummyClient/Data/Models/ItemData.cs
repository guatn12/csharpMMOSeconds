using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DummyClient.Data.Models
{
	public class ItemData
	{
		public int Id { get; set; }
		public string Name { get; set; } = string.Empty;
		public string Description { get; set; } = string.Empty;
		public int Price { get; set; }
		public int HealAmount { get; set; } // 체력 회복량
		public int ManaAmount { get; set; } // 마나 회복량
		public string Grade { get; set; } = "Common"; // 등급
		public bool IsStackable { get; set; } = true; // 중첩 가능

		public int MaxStackCount { get; set; } = 99;
		public string IconPath { get; set; } = string.Empty;
		public List<string> Tags { get; set; } = new();

		// 데이터 검증 메서드
		public bool IsValid()
		{
			return Id > 0 &&
				!string.IsNullOrEmpty( Name ) &&
				Price >= 0 &&
				HealAmount >= 0 &&
				ManaAmount >= 0 &&
				MaxStackCount > 0;
		}
	}
}
