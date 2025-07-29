using DummyClient;
using Microsoft.Extensions.Logging;
using Protocol;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class ClientPacketHandler : IPacketHandler
{
	private readonly ILogger<ClientPacketHandler> _logger;
	private readonly IMovementPacketHandler _movementPacketHandler;
	private readonly IChatPacketHandler _chatPacketHandler;
	private readonly ISystemPacketHandler _systemPacketHandler;
	private readonly IGamePlayPacketHandler _gamePlayPacketHandler;

	public ClientPacketHandler( ILogger<ClientPacketHandler> logger,
		IMovementPacketHandler movementPacketHandler,
		IChatPacketHandler chatPacketHandler,
		ISystemPacketHandler systemPacketHandler,
		IGamePlayPacketHandler gamePlayPacketHandler )
	{
		_logger = logger;
		_movementPacketHandler=movementPacketHandler;
		_chatPacketHandler=chatPacketHandler;
		_systemPacketHandler=systemPacketHandler;
		_gamePlayPacketHandler=gamePlayPacketHandler;
	}

	public IMovementPacketHandler MovementPacketHandler => _movementPacketHandler;
	public IChatPacketHandler ChatPacketHandler => _chatPacketHandler;
	public ISystemPacketHandler SystemPacketHandler => _systemPacketHandler;
	public IGamePlayPacketHandler GamePlayPacketHandler => _gamePlayPacketHandler;
}

public class SystemPacketHandler : ISystemPacketHandler
{
	private readonly ILogger<SystemPacketHandler> _logger;

	public SystemPacketHandler( ILogger<SystemPacketHandler> logger )
	{
		_logger = logger;
	}

	public void On_S_EnterGame( Session session, S_EnterGame packet )
	{
		//Console.WriteLine( $"[FromServer] {packet.Player.Name}님이 게임에 입장했습니다." );
		_logger.LogInformation( "[FromServer] {Name}님이 게임에 입장했습니다.", packet.Player.Name );
	}

	public void On_S_LeaveGame( Session session, S_LeaveGame packet )
	{
		//Console.WriteLine( $"[FromServer] Player({packet.PlayerId})님이 게임을 떠났습니다." );
		_logger.LogInformation( "[FromServer] Player({PlayerId})님이 게임을 떠났습니다.", packet.PlayerId );
	}
}

public class GamePlayPacketHandler : IGamePlayPacketHandler
{
	private readonly ILogger<GamePlayPacketHandler> _logger;

	public GamePlayPacketHandler( ILogger<GamePlayPacketHandler> logger )
	{
		_logger = logger;
	}

	public void On_S_Spawn( Session session, S_Spawn packet )
	{
		foreach(var p in packet.Players)
		{
			//Console.WriteLine( $"[FromServer] Player({p.PlayerId})가 스폰되었습니다. PosInfo ({p.PosInfo.PosX}, {p.PosInfo.PosY}, {p.PosInfo.PosZ})" );
			_logger.LogInformation( "[FromServer] Player({PlayerId})가 스폰되었습니다. PosInfo({PosX}, {PosY}, {PosZ})",
				p.PlayerId, p.PosInfo.PosX, p.PosInfo.PosY, p.PosInfo.PosZ );
		}
	}

	public void On_S_Despawn( Session session, S_Despawn packet )
	{
		foreach(var p in packet.ObjectIds)
		{
			//Console.WriteLine( $"[FromServer] Player({p})가 사라졌습니다." );
			_logger.LogInformation( "[FromServer] Player({p})가 사라졌습니다.", p );
		}
	}
}

public class MovementPacketHandler : IMovementPacketHandler
{
	private readonly ILogger<MovementPacketHandler> _logger;

	public MovementPacketHandler( ILogger<MovementPacketHandler> logger )
	{
		_logger = logger;
	}

	public void On_S_Move( Session session, S_Move packet )
	{
		//Console.WriteLine( $"[FromServer] Player({packet.PlayerId})가 이동했습니다. PosInfo ({packet.PosInfo.PosX}, {packet.PosInfo.PosY}, {packet.PosInfo.PosZ})" );
		_logger.LogInformation( "[FromServer] Player({PlayerId})가 이동했습니다. PosInfo({PosX}, {PosY}, {PosZ})",
			packet.PlayerId, packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ );
	}
}

public class ChatPacketHandler : IChatPacketHandler
{
	private readonly ILogger<ChatPacketHandler> _logger;

	public ChatPacketHandler( ILogger<ChatPacketHandler> logger )
	{
		_logger = logger;
	}

	public void On_S_Chat( Session session, S_Chat packet )
	{
		if(Program.Session != null && Program.Session.DummyId != packet.PlayerId)
			_logger.LogInformation( "[FromServer] Player({PlayerId}): {Message}", packet.PlayerId, packet.Message );
			//Console.WriteLine( $"[FromServer] Player({packet.PlayerId}): {packet.Message}" );
	}
}