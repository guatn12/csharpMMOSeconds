using Protocol;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// 클라이언트가 수신하는 패킷들의 핸들러 메서드 시그니처 정의
public interface IPacketHandler
{
	void On_S_EnterGame( Session session, S_EnterGame packet );
	void On_S_LeaveGame( Session session, S_LeaveGame packet );
	void On_S_Spawn( Session session, S_Spawn packet );
	void On_S_Despawn( Session session, S_Despawn packet );
	void On_S_Move( Session session, S_Move packet );
	void On_S_Chat( Session session, S_Chat packet );
}