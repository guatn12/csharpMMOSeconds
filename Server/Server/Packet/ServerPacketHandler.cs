using Microsoft.Extensions.Logging;
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
	private readonly IMovementPacketHandler _movementPacketHandler;
	private readonly IChatPacketHandler _chatPacketHandler;

	public ServerPacketHandler(
		IMovementPacketHandler movementPacketHandler, IChatPacketHandler chatPacketHandler )
	{
		_movementPacketHandler = movementPacketHandler;
		_chatPacketHandler = chatPacketHandler;
	}

	public IMovementPacketHandler MovementPacketHandler => _movementPacketHandler;
	public IChatPacketHandler ChatPacketHandler => _chatPacketHandler;
}

public class MovementPacketHandler : IMovementPacketHandler
{
	private readonly ILogger<MovementPacketHandler> _logger;

	public MovementPacketHandler(ILogger<MovementPacketHandler> logger)
	{
		_logger = logger;
	}

	public void On_C_Move( Session session, C_Move packet )
	{
		GameSession gameSession = session as GameSession;
		if(gameSession == null) return;

		_logger.LogInformation( "[C_Move] Player({SessionId}) -> PosInfo:{PosX}, {PosY}, {PosZ}", gameSession.SessionId,
			packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ );
		//Console.WriteLine( $"[C_Move] Player({gameSession.SessionId}) -> PosInfo:{packet.PosInfo.PosX}, {packet.PosInfo.PosY}, {packet.PosInfo.PosZ}" );
	}
}

public class ChatPacketHandler : IChatPacketHandler
{
	private readonly ILogger<ChatPacketHandler> _logger;

	public ChatPacketHandler(ILogger<ChatPacketHandler> logger)
	{
		_logger = logger;
	}

	public void On_C_Chat( Session session, C_Chat packet )
	{
		GameSession gameSession = session as GameSession;
		if(gameSession == null)
			return;

		_logger.LogInformation( "[C_Chat] Player({SessionId}): {Message}", gameSession.SessionId, packet.Message );
		//Console.WriteLine( $"[C_Chat] Player({gameSession.SessionId}): {packet.Message}" );


		S_Chat chat = new S_Chat
		{
			PlayerId = gameSession.SessionId,
			Message = $"Echo: {packet.Message}",
		};

		gameSession.Send( chat );
	}
}