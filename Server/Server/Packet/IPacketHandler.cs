using Protocol;
using ServerCore;

// 서버가 수신하는 패킷들의 핸들러 메서드 시그니처 정의
public interface IPacketHandler
{
	public IMovementPacketHandler MovementPacketHandler { get; }
	public IChatPacketHandler ChatPacketHandler { get; }
}

public interface IMovementPacketHandler
{
	public void On_C_Move( Session session, C_Move packet );
}

public interface IChatPacketHandler
{
	public void On_C_Chat( Session session, C_Chat packet );
}