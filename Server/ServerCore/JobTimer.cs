using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ServerCore
{
	public struct JobTimerElem
	{
		public IJob Job;
		public JobTimerToken Token;		// null이면 취소 불가 작업
	}

	public class JobTimer
	{
		private readonly PriorityQueue<JobTimerElem, long> _timerQueue = new PriorityQueue<JobTimerElem, long>();

		public void Push(IJob job, int tickAfter)
		{
			long execTime = Environment.TickCount64 + tickAfter;
			_timerQueue.Enqueue( new JobTimerElem { Job = job }, execTime );
		}

		public void Push(IJob job, int tickAfter, out JobTimerToken token )
		{
			token = new JobTimerToken();
			long execTime = Environment.TickCount64 + tickAfter;
			_timerQueue.Enqueue( new JobTimerElem { Job = job, Token = token }, execTime );
		}

		public void Flush(ConcurrentQueue<IJob> jobQueue)
		{
			long now = Environment.TickCount64;
			while(_timerQueue.TryPeek(out _, out long execTime) && execTime <= now)
			{
				JobTimerElem elem = _timerQueue.Dequeue();
				if(elem.Token != null && elem.Token.IsCanceled)
					continue;
				jobQueue.Enqueue( elem.Job );
			}
		}
	}
}
