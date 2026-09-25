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
	public class PlayerStateEntity
	{
		[Key]
		[Column("player_state_id")]
		public long PlayerStateId { get; set; }

		[Column("player_id")]
		public long PlayerId { get; set; }

		[Column( "version" )]
		public long Version { get; set; } = 1;

		[Column( "State_data", TypeName = "jsonb" )]
		public string StateDataJson { get; set; } = "{}";

		[Column( "created_at" )]
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		[Column( "last_updated" )]
		public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

		[NotMapped]
		public PlayerStateModel StateData
		{
			get => string.IsNullOrEmpty(StateDataJson)
				? new PlayerStateModel()
				: JsonSerializer.Deserialize<PlayerStateModel>(StateDataJson)
				?? new PlayerStateModel();
			set => StateDataJson = JsonSerializer.Serialize( value );
		}

		[ForeignKey( "PlayerId" )]
		public PlayerEntity Player { get; set; } = null!;
	}

	public class PlayerStateModel
	{
		public int MapId { get; set; }
		public float PosX { get; set; }
		public float PosY { get; set; }
		public float PosZ { get; set; }
		public float RotationX { get; set; }
		public float RotationY { get; set; }
		public float RotationZ { get; set; }
		public int CurrentHp { get; set; } = 100;
		public int CurrentMp { get; set; } = 100;
		public int CreatureState {  get; set; }
	}
}
