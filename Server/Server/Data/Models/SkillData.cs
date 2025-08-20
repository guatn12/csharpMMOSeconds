using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data.Models
{
	public class SkillData
	{
		public int Id { get; set; }
		public string Name { get; set; } = string.Empty;
		public string Description { get; set; } = string.Empty;
		public int ManaCost { get; set; }
		public int Damage {  get; set; }
		public float CooldownSeconds { get; set; }
		public float CastTime { get; set; }
		public float Range { get; set; }
		public string SkillType { get; set; } = "Attack"; // Attack, Heal, Buff, Debuff

		// 데이터 검증 메서드
		public bool IsValid()
		{
			return Id > 0 &&
				!string.IsNullOrEmpty( Name ) &&
				ManaCost >= 0 &&
				CooldownSeconds >= 0 &&
				CastTime >= 0 &&
				Range >= 0;
		}
	}
}
