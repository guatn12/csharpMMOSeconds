using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Server.Database.Entities
{
	public class InventoryEntity
	{
		[Key]
		[Column("inventory_id")]
		public long InventoryId { get; set; }

		[Column( "player_id" )]
		public long PlayerId { get; set; }

		[Column( "max_slots" )]
		public int MaxSlots { get; set; } = 50;

		[Column( "version" )]
		public int Version { get; set; } = 1; // 낙관적 동시성 제어용?

		[Column( "last_updated" )]
		public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

		[Column("created_at")]
		public DateTime CreatedAt { get; set;} = DateTime.UtcNow;

		// JSONB 컬럼 - 인벤토리 데이터
		[Column( "inventory_data", TypeName = "jsonb" )]
		public string InventoryDataJson { get; set; } = "{}";

		// JSONB 데이터 접근을 위한 프로퍼티
		[NotMapped]
		public InventoryModel InventoryData
		{
			get => string.IsNullOrEmpty( InventoryDataJson )
				? new InventoryModel()
				: JsonSerializer.Deserialize<InventoryModel>( InventoryDataJson )
				?? new InventoryModel();
			set => InventoryDataJson = JsonSerializer.Serialize( value );
		}

		// 관계 설정
		[ForeignKey( "PlayerId" )]
		public PlayerEntity Player { get; set; } = null!;
	}

	public class InventoryModel
	{
		public List<InventoryItem> Items { get; set; } = new List<InventoryItem>();
		public long Gold { get; set; } = 0;
		public DateTime LastSorted { get; set; } = DateTime.UtcNow;
		public Dictionary<string, object> ExtensionData { get; set; } = new Dictionary<string, object>();
	}

	public class InventoryItem
	{
		public int ItemId { get; set; }
		public int Quantity { get; set; } = 1;
		public int Slot {  get; set; }
		public EnhancementData Enhancement { get; set; }
		public Dictionary<string, double> Options { get; set; } = new Dictionary<string, double>();
		public DateTime? AcquiredAt { get; set; } = DateTime.UtcNow;
		public string CustomName { get; set; }
	}

	public class EnhancementData
	{
		public int Level { get; set; } = 0;
		public double SuccessRate { get; set; } = 1.0;
		public DateTime? EnhancedAt { get; set; }
		public long EnhancementCost { get; set; } = 0;
	}
}
