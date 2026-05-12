using System;
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

		public Task<T> PushAsync<T>(Func<ValueTask<T>> work)
		{
			var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

			if(CanAcceptJob() == false)
			{
				tcs.SetException(new InvalidOperationException( "작업을 처리할 수 없는 상태입니다." ) );
				return tcs.Task;
			}

			DelegateJob job = _jobQueueManager.JobPool.Get<DelegateJob>();
			job.Initialize( async () =>
			{
				try
				{
					T result = await work();
					tcs.TrySetResult( result );
				}
				catch(Exception ex)
				{
					tcs.TrySetException( ex );
				}
			} );

			Push( job );
			return tcs.Task;
		}

		public Task PushAsync(Func<ValueTask> work)
		{
			var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			if(CanAcceptJob() == false)
			{
				tcs.SetException(new InvalidOperationException( "작업을 처리할 수 없는 상태입니다." ) );
				return tcs.Task;
			}

			DelegateJob job = _jobQueueManager.JobPool.Get<DelegateJob>();
			job.Initialize( async () =>
			{
				try
				{
					await work();
					tcs.TrySetResult();
				}
				catch(Exception ex)
				{
					tcs.TrySetException( ex );
				}
			} );

			Push( job );
			return tcs.Task;
		}

		protected virtual bool CanAcceptJob()
		{
			// 기본적으로 항상 작업을 수락할 수 있다고 가정.
			// 필요에 따라 파생 클래스(BaseRoom)에서 오버라이드하여 특정 조건에서 작업 수락 여부 결정 가능.
			return true;
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

		private void SafeOnProcessJobStart()
		{
			try
			{
				OnProcessJobsStart();
			}
			catch(Exception ex) when (ExceptionPolicy.IsCritical(ex) == false)
			{
				OnLifecycleHookFailed( nameof(OnProcessJobsStart), ex );
			}
		}

		/// <summary>
		/// 빈메서드 - 파생 클래스에서 오버라이드하여 작업 처리 종료시 동작 구현 가능.
		/// 현재는 디버깅용으로 처리.
		/// </summary>
		protected virtual void OnProcessJobsEnd() { }

		private void SafeOnProcessJobEnd()
		{
			try
			{
				OnProcessJobsEnd();
			}
			catch(Exception ex) when (ExceptionPolicy.IsCritical(ex) == false)
			{
				OnLifecycleHookFailed( nameof( OnProcessJobsEnd ), ex );
			}
		}

		protected virtual void OnJobFailed(IJob job, Exception ex)
		{
			// 기본 동작 없음 - 파생 클래스에서 로깅/세션 정리 등을 오버라이드
			// JobQueueManager catch가 최후 방어선으로 로그를 남김
		}

		protected virtual void OnLifecycleHookFailed(string hookName, Exception ex )
		{
			// 기본 동작 없음 - 파생 클래스가 필요하면 최소 로깅만 수행
			// 이 hook 안에서는 외부 I/O나 복잡한 복구 로직을 수행하지 않는다.
		}

		async ValueTask IJobOwner.ProcessJobsAsync()
		{
			// 작업 처리중 상태로 변경.
			Interlocked.Exchange( ref _isProcessing, 1 );
			SafeOnProcessJobStart();

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

				try
				{
					await job.ExecuteAsync();
				}
				catch(Exception ex) when(ExceptionPolicy.IsCritical( ex ) == false)
				{
					try
					{
						OnJobFailed( job, ex );
					}
					catch(Exception hookEx)
					{
						// OnJobFailed 자체의 예외로 인해 Owner 루프가 정지하는 것을 방지
						// 원본 예외(ex)는 이미 when 필터에서 non-critical로 판정된 상태.
						// hookEx는 삼키되, 상위 워커의 포괄 catch가 최후 방어선
					}
				}
				finally
				{
					job.Clear();
					_jobQueueManager.JobPool.Return( job );
				}
			}

			// 작업 처리가 모두 끝남.
			SafeOnProcessJobEnd();
		}
	}
}
