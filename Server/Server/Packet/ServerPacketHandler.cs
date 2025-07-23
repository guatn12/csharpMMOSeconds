using Protocol;
using Server;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

public class ServerPacketHandler : IPacketHandler
{
	public IMovementPacketHandler MovementPacketHandler => new MovementPacketHandler();
	public IChatPacketHandler ChatPacketHandler => new ChatPacketHandler();
}

public class MovementPacketHandler : IMovementPacketHandler
{
	public void On_C_Move( Session session, C_Move packet )
	{
		GameSession gameSession = session as GameSession;
		if(gameSession == null) return;

		Console.WriteLine( $"[C_Move] Player({gameSession.SessionId}) -> PosInfo:{packet.PosInfo.PosX}, {packet.PosInfo.PosY}, {packet.PosInfo.PosZ}" );
	}
}

public class ChatPacketHandler : IChatPacketHandler
{
	public void On_C_Chat( Session session, C_Chat packet )
	{
		GameSession gameSession = session as GameSession;
		if(gameSession == null)
			return;

		Console.WriteLine( $"[C_Chat] Player({gameSession.SessionId}): {packet.Message}" );
	}
}