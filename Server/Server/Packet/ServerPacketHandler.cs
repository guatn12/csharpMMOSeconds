using Microsoft.Extensions.Logging;
using Protocol;
using Server;
using Server.Room;
using Server.Room.Jobs;
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
		if(gameSession == null) 
		{
			_logger.LogCritical("CRITICAL: Received C_Move from non-GameSession - Server integrity compromised");
			throw new InvalidOperationException("Non-GameSession sent C_Move packet");
		}

		// 룸 검증 - Critical 이슈
		IRoom room = gameSession.CurrentRoom;
		if(room == null)
		{
			_logger.LogCritical("CRITICAL: Player({SessionId}) sent C_Move but not in any room - State desync detected", 
				gameSession.SessionId);
			throw new InvalidOperationException($"Player {gameSession.SessionId} not in room but sent move packet");
		}

		_logger.LogInformation( "[C_Move] Player({SessionId}) -> PosInfo:{PosX}, {PosY}, {PosZ}", gameSession.SessionId,
			packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ );

		// TODO : 플레이어 상태 체크

		// Generic 라우팅 사용
		gameSession.RouteToRoom(packet);
		
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
		{
			_logger.LogCritical("CRITICAL: Received C_Chat from non-GameSession - Server integrity compromised");
			throw new InvalidOperationException("Non-GameSession sent C_Chat packet");
		}

		// 룸 검증 - Critical 이슈
		IRoom room = gameSession.CurrentRoom;
		if(room == null)
		{
			_logger.LogCritical("CRITICAL: Player({SessionId}) sent C_Chat but not in any room - State desync detected", 
				gameSession.SessionId);
			throw new InvalidOperationException($"Player {gameSession.SessionId} not in room but sent chat packet");
		}

		_logger.LogInformation( "[C_Chat] Player({SessionId}): {Message}", gameSession.SessionId, packet.Message );

		// Generic 라우팅 사용
		gameSession.RouteToRoom(packet);
	}
}