using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseLib.Services
{
	public static class PlayerPersistenceCacheKeys
	{
		public static string Account( long accountId ) => $"account:{accountId}";
		public static string Inventory( long playerId ) => $"inventory:{playerId}";
		public static string Equipment( long playerId ) => $"equipment:{playerId}";
		public static string State( long playerId ) => $"player-state:{playerId}";
	}
}
