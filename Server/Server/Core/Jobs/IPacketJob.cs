using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Server.Core.Session;
using Server.Room;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core.Jobs
{
	public interface IPacketJob<T> : IJob where T : IMessage
	{
		// Job 실행에 필요한 모든 컨텍스트를 초기화합니다.
		void Initialize(GameSession session, IRoom room, T packet, ILogger logger);

		// 패킷 처리 핸들러를 설정합니다.
		void SetHandler( Func<GameSession, IRoom, IMessage, ILogger, ValueTask> handler );

		// 모든 멤버 변수를 초기화하여 메모리 누수를 방지합니다.
		void Clear();
	}
}
