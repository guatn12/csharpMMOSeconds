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
		if(gameSession == null) return;

		_logger.LogInformation( "[C_Move] Player({SessionId}) -> PosInfo:{PosX}, {PosY}, {PosZ}", gameSession.SessionId,
			packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ );

		// TODO : 플레이어 상태 체크

		IRoom room = gameSession.CurrentRoom;
		int prevJobCount = room.JobQueue.Count;
		room.JobQueue.Enqueue( new MoveJob( gameSession, gameSession.CurrentRoom, packet, _logger ) );

		if(prevJobCount == 0)
		{
			_ = JobQueueManager.Instance.PushAsync( room );
		}
		
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

		IRoom room = gameSession.CurrentRoom;
		int prevJobCount = room.JobQueue.Count;
		room.JobQueue.Enqueue( new ChatJob( gameSession, gameSession.CurrentRoom, packet, _logger ) );

		if(prevJobCount == 0)
		{
			_ = JobQueueManager.Instance.PushAsync( room );
		}

		//S_Chat chat = new S_Chat
		//{
		//	PlayerId = gameSession.SessionId,
		//	Message = $"Echo: {packet.Message}",
		//};

		//gameSession.Send( chat );
	}
}