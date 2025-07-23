using DummyClient;
using Protocol;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class ClientPacketHandler : IPacketHandler
{
	public IMovementPacketHandler MovementPacketHandler => new MovementPacketHandler();

	public IChatPacketHandler ChatPacketHandler => new ChatPacketHandler();

	public ISystemPacketHandler SystemPacketHandler => new SystemPacketHandler();

	public IGamePlayPacketHandler GamePlayPacketHandler => new GamePlayPacketHandler();
}

public class SystemPacketHandler : ISystemPacketHandler
{
	public void On_S_EnterGame( Session session, S_EnterGame packet )
	{
		Console.WriteLine( $"[FromServer] {packet.Player.Name}님이 게임에 입장했습니다." );
	}

	public void On_S_LeaveGame( Session session, S_LeaveGame packet )
	{
		Console.WriteLine( $"[FromServer] Player({packet.PlayerId})님이 게임을 떠났습니다." );
	}
}

public class GamePlayPacketHandler : IGamePlayPacketHandler
{
	public void On_S_Spawn( Session session, S_Spawn packet )
	{
		foreach(var p in packet.Players)
		{
			Console.WriteLine( $"[FromServer] Player({p.PlayerId})가 스폰되었습니다. PosInfo ({p.PosInfo.PosX}, {p.PosInfo.PosY}, {p.PosInfo.PosZ})" );
		}
	}

	public void On_S_Despawn( Session session, S_Despawn packet )
	{
		foreach(var p in packet.ObjectIds)
		{
			Console.WriteLine( $"[FromServer] Player({p})가 사라졌습니다." );
		}
	}
}

public class MovementPacketHandler : IMovementPacketHandler
{
	public void On_S_Move( Session session, S_Move packet )
	{
		Console.WriteLine( $"[FromServer] Player({packet.PlayerId})가 이동했습니다. PosInfo ({packet.PosInfo.PosX}, {packet.PosInfo.PosY}, {packet.PosInfo.PosZ})" );
	}
}

public class ChatPacketHandler : IChatPacketHandler
{
	public void On_S_Chat( Session session, S_Chat packet )
	{
		if(Program.Session != null && Program.Session.DummyId != packet.PlayerId)
			Console.WriteLine( $"[FromServer] Player({packet.PlayerId}): {packet.Message}" );
	}
}