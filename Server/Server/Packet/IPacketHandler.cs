using Protocol;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// 서버가 수신하는 패킷들의 핸들러 메서드 시그니처 정의
public interface IPacketHandler
{
	void On_C_Move( Session session, C_Move packet );
	void On_C_Chat( Session session, C_Chat packet );
}
