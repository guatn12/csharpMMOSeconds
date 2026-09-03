using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DatabaseLib.Entities
{
	public class EquipmentEntity
	{
		[Key]
		[Column( "equipment_id" )]
		public long EquipmentId { get; set; }

		[Column( "player_id" )]
		public long PlayerId { get; set; }

		[Column( "version" )]
		public long Version { get; set; } = 1; // 낙관적 동시성 제어용?

		[Column( "last_updated" )]
		public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

		[Column( "created_at" )]
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		// JSONB 컬럼 - 장착 장비 데이터
		[Column( "equipment_data", TypeName = "jsonb" )]
		public string EquipmentDataJson { get; set; } = "{}";

		// JSONB 데이터 접근을 위한 프로퍼티
		[NotMapped]
		public Dictionary<int, long> EquipmentData 
		{
			get => string.IsNullOrEmpty( EquipmentDataJson )
				? new Dictionary<int, long>()
				: JsonSerializer.Deserialize<Dictionary<int, long>>( EquipmentDataJson )
				?? new Dictionary<int, long>();

			set => EquipmentDataJson = JsonSerializer.Serialize( value );
		}

		// 관계 설정
		[ForeignKey( "PlayerId" )]
		public PlayerEntity Player { get; set; } = null!;
	}
}
