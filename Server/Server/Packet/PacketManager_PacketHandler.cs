using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Packet
{
	public partial class PacketManager_PacketHandler
	{
		// partial 메소드 구현부
		partial void On_C_MOVE( PacketSession session, Protocol.C_Move packet )
		{
			GameSession gameSession = session as GameSession;
			if(gameSession == null) return;
			Console.WriteLine( $"[C_MOVE] Player({gameSession.SessionId}) -> PosX:{packet.PosInfo.PosX}" );
		}

		partial void On_C_CHAT( PacketSession session, Protocol.C_Chat packet )
		{
			GameSession gameSession = session as GameSession;
			if(gameSession == null) return;
			Console.WriteLine( $"[C_CHAT] Player({gameSession.SessionId}): {packet.Message}" );
		}
	}
}
