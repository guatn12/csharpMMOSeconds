using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core.Session
{
	public enum SessionState
	{
		Connected = 0,
		// 1, 2 예약 (Phase 4: Authenticating, Authenticated)
		EnteringGame = 10,
		InRoom = 20,
		Transferring = 30,
		Disconnecting = 90,
		Disconnected = 99,
	}
}
