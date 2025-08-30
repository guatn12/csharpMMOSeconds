using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data.Entities
{
	public class PlayerEntity
	{
		[Key]
		public long PlayerId { get; set; }

		[Required]
		public string PlayerName { get; set; }

		public int Level { get; set; } = 1;
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	}
}
