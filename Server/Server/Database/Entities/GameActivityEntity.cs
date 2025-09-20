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
	[Table("game_activity")]
	public class GameActivityEntity
	{
		[Key]
		[Column("activity_id")]
		public long ActivityId { get; set; }

		[Column("player_id")]
		public long PlayerId { get; set; }

		[Required]
		[MaxLength( 50 )]
		[Column( "activity_type" )]
		public string ActivityType { get; set; } = string.Empty;

		[Column( "created_at" )]
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		// JSONB 컬럼 - 활동 데이터
		[Column( "activity_data", TypeName = "jsonb" )]
		public string ActivityDataJson { get; set; } = "{}";

		// JSONB 데이터 접근을 위한 프로퍼티
		[NotMapped]
		public Dictionary<string, object> ActivityData
		{
			get => string.IsNullOrEmpty( ActivityDataJson )
				? new Dictionary<string, object>()
				: JsonSerializer.Deserialize<Dictionary<string, object>>( ActivityDataJson )
				?? new Dictionary<string, object>();
			set => ActivityDataJson = JsonSerializer.Serialize( value );
		}

		// 관계 설정
		[ForeignKey( "PlayerId" )]
		public PlayerEntity Player { get; set; } = null!;
	}

	// 활동 타입 상수
	public static class ActivityTypes
	{
		public const string LOGIN = "LOGIN";
		public const string LOGOUT = "LOGOUT";
		public const string ITEM_ACQUIRE = "ITEM_ACQUIRE";
		public const string ITEM_USE = "ITEM_USE";
		public const string TRADE = "TRADE";
		public const string PVP = "PVP";
		public const string QUEST_COMPLETE = "QUEST_COMPLETE";
		public const string LEVEL_UP = "LEVEL_UP";
		public const string CHAT = "CHAT";
		public const string ROOM_ENTER = "ROOM_ENTER";
		public const string ROOM_LEAVE = "ROOM_LEAVE";
	}

	// 활동 데이터 모델들 (타입별 구조화)
	public class LoginActivityData
	{
		public string LoginIp { get; set; } = string.Empty ;
		public string DeviceInfo {  get; set; } = string.Empty ;
		public long SessionDurationMinutes { get; set; } = 0;
	}

	public class ItemActivityData
	{
		public int ItemId { get; set; }
		public int Quantity { get; set; }
		public string Source { get; set; } = string.Empty; // "DROP", "TRADE", "QUEST", "PURCHASE"
		public string Location { get; set; } = string.Empty;
		public Dictionary<string, object> AdditionalInfo { get; set; } = new();
	}

	public class TradeActivityData
	{
		public long TargetPlayerId { get; set; }
		public string TargetPlayerName { get; set; } = string.Empty;
		public List<InventoryItem> ItemsGiven { get; set; } = new();
		public List<InventoryItem> ItemsReceived { get; set; } = new();
		public long GoldGiven { get; set; } = 0;
		public long GoldReceived { get; set; } = 0;
		public string TradeResult { get; set; } = "SUCCESS"; // "SUCCESS", "CANCELLED", "FAILED"
	}

	public class PvpActivityData
	{
		public long OpponentId { get; set; }
		public string OpponentName { get; set; } = string.Empty;
		public string Result { get; set; } = string.Empty; // "WIN", "LOSE", "DRAW"
		public int DamageDealt { get; set; } = 0;
		public int DamageReceived { get; set; } = 0;
		public long ExpGained { get; set; } = 0;
		public TimeSpan BattleDuration { get; set; }
		public string Location { get; set; } = string.Empty;
	}
}
