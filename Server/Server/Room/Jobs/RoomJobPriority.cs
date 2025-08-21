using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Jobs
{
	public static class RoomJobPriority
	{
		public const int Critical = 1000;

		public const int High = 100;

		public const int Normal = 50;

		public const int Low = 10;

		public const int VeryLow = 1;
	}
}
