using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Server.Data.Entities
{
	public class PlayerEntity
	{
		[Key]
		[Column("player_id")]
		public long PlayerId { get; set; }

		[Required]
		[MaxLength(50)]
		[Column("player_name")]
		public string PlayerName { get; set; }

		[Column("level")]
		public int Level { get; set; } = 1;

		[Column( "experience" )]
		public long Experience { get; set; } = 0;

		[MaxLength( 255 )]
		[Column( "login_token" )]
		public string? LoginToken { get; set; }

		[Column("last_login_at")]
		public DateTime? LastLoginAt { get; set; }

		[Column( "total_play_time_minutes" )]
		public long TotalPlayTimeMinutes { get; set; } = 0;

		[Column( "created_at" )]
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		[Column( "updated_at" )]
		public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

		// JSONB 컬럼 - 플레이어 설정 및 확장 데이터
		[Column( "player_settings", TypeName = "jsonb" )]
		public string PlayerSettingsJson { get; set; } = "{}";

		// JSONB 데이터 접근을 위한 프로퍼티
		[NotMapped]
		public PlayerSettingsModel PlayerSettings
		{
			get => string.IsNullOrEmpty( PlayerSettingsJson )
				? new PlayerSettingsModel()
				: JsonSerializer.Deserialize<PlayerSettingsModel>( PlayerSettingsJson )
				?? new PlayerSettingsModel();

			set => PlayerSettingsJson = JsonSerializer.Serialize( value );
		}
	}

	// 플레이어 설정 모델
	public class PlayerSettingsModel
	{
		public List<long> FriendList { get; set; } = new();
		public Dictionary<string, object> GameSettings { get; set; } = new();
		public List<int> CompletedAchievements { get; set; } = new();
		public Dictionary<string, int> Statistics { get; set; } = new();
		public DateTime LastSettingsUpdate { get; set; } = DateTime.UtcNow;
	}
}
