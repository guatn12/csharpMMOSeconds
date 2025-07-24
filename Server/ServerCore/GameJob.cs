using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public class GameJob
	{
		public Action Action { get; private set; } // 실행할 코드 (델리게이트)
		public JobPriority Priority { get; private set; } // 우선순위
		public DateTime EnqueueTime { get; private set; } // Job이 큐에 저장된 시간(기아 방지용)

		public GameJob(Action action, JobPriority priority)
		{
			Action = action; 
			Priority = priority;
			EnqueueTime = DateTime.UtcNow; // Job 생성 시 UTC기준 현재 시간 저장.
		}
	}
}
