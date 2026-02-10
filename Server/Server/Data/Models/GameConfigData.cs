using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data.Models
{
	public class GameConfigData
	{
		public int ViewDistance { get; set; } = 50;
		public int BroadCastRange { get; set; } = 100;
		public int PlayerDefaultMoveSpeed { get; set; } = 5;
		public int MonsterDespawnDelaySeconds { get; set; } = 3;

		public bool IsValid()
		{
			return ViewDistance > 0 &&
				   BroadCastRange > 0 &&
				   PlayerDefaultMoveSpeed > 0 && MonsterDespawnDelaySeconds > 0;
		}
	}
}
