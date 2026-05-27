using Server.Core.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room
{
	public interface IRoomTransitionCoordinator
	{
		Task<RoomTransitionResult> ChangeRoomAsync( IClientSession session, int targetRoomId, RoomTransitionReason reason );
		bool TryGetActiveTransition( long sessionId, out RoomTransitionContext context );
		bool CancelTransition( long sessionId, RoomTransitionCancelReason reason );
	}
}
