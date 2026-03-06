using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;


namespace ServerCore
{
	public class JobSerializer : IJobOwner
	{
		protected readonly IJobQueueManager _jobQueueManager;
		private ConcurrentQueue<IJob> _jobQueue = new ConcurrentQueue<IJob>();
		private readonly JobTimer _jobTimer = new JobTimer();
		private int _isProcessing = 0;

		public JobSerializer( IJobQueueManager jobQueueManager )
		{
			_jobQueueManager = jobQueueManager;
		}

		public void Push(IJob job)
		{
			_jobQueue.Enqueue(job);

			// 현재 워커에 의해 작업중인 상태인지 확인.
			if(Interlocked.CompareExchange( ref _isProcessing, 1, 0 ) == 0)
			{
				// 작업이 진행중이 아니므로 전역 큐에 자신을 등록.
				_ = _jobQueueManager.RegisterAsync( this );
			}

			// 작업이 이미 진행중이라면 작업 리스트에 추가만 하고 넘어감.
		}

		protected void ScheduleTimer(IJob job, int tickAfter)
		{
			_jobTimer.Push( job, tickAfter );
		}

		protected void ScheduleTimer(IJob job, int tickAfter, out JobTimerToken token)
		{
			_jobTimer.Push( job, tickAfter, out token );
		}

		/// <summary>
		/// 빈메서드 - 파생 클래스에서 오버라이드하여 작업 처리 시작시 동작 구현 가능.
		/// 현재는 디버깅용으로 처리.
		/// </summary>
		protected virtual void OnProcessJobsStart() { }

		/// <summary>
		/// 빈메서드 - 파생 클래스에서 오버라이드하여 작업 처리 종료시 동작 구현 가능.
		/// 현재는 디버깅용으로 처리.
		/// </summary>
		protected virtual void OnProcessJobsEnd() { }

		async ValueTask IJobOwner.ProcessJobsAsync()
		{
			// 작업 처리중 상태로 변경.
			Interlocked.Exchange( ref _isProcessing, 1 );
			OnProcessJobsStart();

			_jobTimer.Flush( _jobQueue );

			while(true)
			{
				if(_jobQueue.TryDequeue( out var job ) == false)
				{
					if(_jobQueue.Count == 0)
					{
						// 더이상 처리할 작업이 없으므로 처리중 상태를 해제.
						Interlocked.Exchange( ref _isProcessing, 0 );

						// 작업이 새로 추가된 경우를 대비하여 다시 상태를 확인.
						if(_jobQueue.Count > 0)
						{
							if(Interlocked.CompareExchange( ref _isProcessing, 1, 0 ) == 0)
							{
								continue;
							}
						}

						break;
					}

					continue;
				}

				await job.ExecuteAsync();
				job.Clear();
				_jobQueueManager.JobPool.Return( job );
			}

			// 작업 처리가 모두 끝남.
			OnProcessJobsEnd();
		}
	}
}
