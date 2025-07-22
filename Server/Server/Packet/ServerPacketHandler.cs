using Protocol;
using Server;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class ServerPacketHandler : IPacketHandler
{
	public void On_C_Chat( Session session, C_Chat packet )
	{
		GameSession gameSession = session as GameSession;
		if(gameSession == null)
			return;

		Console.WriteLine( $"[C_Chat] Player({gameSession.SessionId}): {packet.Message}" );
	}

	public void On_C_Move( Session session, C_Move packet )
	{
		GameSession gameSession = session as GameSession;
		if(gameSession == null) return;

		Console.WriteLine( $"[C_Move] Player({gameSession.SessionId}) -> PosInfo:{packet.PosInfo.PosX}, {packet.PosInfo.PosY}, {packet.PosInfo.PosZ}" );
	}
}
