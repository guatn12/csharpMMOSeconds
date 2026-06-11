using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core.Session
{
	/// <summary>
	/// 세션 전용 잡 큐. SYSTEM 패킷을 세션 단위로 직렬화 한다.
	/// 종료(Disconnecting 이상) 상태에서는 신규 잡을 거부해 disconnect 후 진입을 막는다.
	/// </summary>
	public sealed class SessionJobQueue : JobSerializer
	{
		private readonly IClientSession _owner;

		public SessionJobQueue(IJobQueueManager jobQueueManager, IClientSession owner)
			: base(jobQueueManager)
		{
			_owner = owner;
		}

		// PushAsync 경로의 입구 차단(순수 Push 경로는 OnRecvPacket의 State 가드가 담당)
		protected override bool CanAcceptJob()
		{
			return _owner.State < SessionState.Disconnecting;
		}
	}
}
