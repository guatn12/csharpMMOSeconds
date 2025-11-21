using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.DTOs
{
	/// <summary>
	/// 전투 결과를 담는 DTO (Data Transfer Object)
	/// </summary>
	public class CombatResults
	{
		public long AttackerId { get; set; }
		public long TargetId { get; set; }
		public int Damage { get; set; }
		public bool IsCritical { get; set; }
		public int TargetCurrentHP { get; set; }
		public bool TargetDied { get; set; }
	}
}
