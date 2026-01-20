using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ServerCore
{
	public class JobQueueManager : IJobQueueManager
	{
		//public static JobQueueManager Instance { get; } = new JobQueueManager();
		private readonly ILogger<JobQueueManager> _logger;

		private List<Task> _workerTasks = new List<Task>();
		private Channel<IJobOwner> _jobQueue;
		private CancellationTokenSource _cancellationTokenSource;
		public JobPool JobPool { get; private set; } = new JobPool();

		public JobQueueManager( ILogger<JobQueueManager> logger ) 
		{
			_logger = logger;
		}

		//public static void Initialize( ILogger<JobQueueManager> logger )
		//{
		//	_logger = logger;
		//}

		public void Start( int workerCount, int channelCapacity = 1000 )
		{
			// 이미 시작된 경우 중복 방지.
			if(0 < _workerTasks.Count)
			{
				_logger.LogWarning( "JobQueueManager is already running." );
				return;
			}

			_cancellationTokenSource = new CancellationTokenSource();
			// BlockingCollection은 기본적으로 ConcurrentQueue를 사용합니다.
			var options = new BoundedChannelOptions(channelCapacity)
			{
				FullMode = BoundedChannelFullMode.Wait, // 채널이 가득 차면 비동기 대기
                SingleReader = false,   // 여러 워커가 읽을 수 있음
                SingleWriter = false    // 여러 워커가 쓸 수 있음.
            };
			_jobQueue = Channel.CreateBounded<IJobOwner>( options ); // 타입 변경

			for(int i = 0; i < workerCount; i++)
			{
				var task = Task.Factory.StartNew(() => WorkerLoopAsync(_cancellationTokenSource.Token),
					_cancellationTokenSource.Token, TaskCreationOptions.LongRunning,
					TaskScheduler.Default);
				_workerTasks.Add( task );
			}

			_logger.LogInformation( $"JobQueueManager started with {workerCount} workers." );
		}

		public async Task StopAsync()
		{
			if(_cancellationTokenSource == null || _cancellationTokenSource.IsCancellationRequested)
			{
				return;
			}

			_logger.LogInformation( "JobQueueManager stopping..." );

			_jobQueue.Writer.Complete();

			_cancellationTokenSource.Cancel();

			await Task.WhenAll( _workerTasks );
			_workerTasks.Clear();

			// Channel 리소스 정리
			_jobQueue = null;
			
			_cancellationTokenSource.Dispose();
			_cancellationTokenSource = null;

			_logger.LogInformation( "All Job Workers Stopped." );
		}

		public async ValueTask RegisterAsync(IJobOwner jobOwner)
		{
			if(jobOwner == null) return;

			// CancellationTokenSource가 null이거나 종료 중이면 작업을 추가하지 않음.
			if(_cancellationTokenSource == null || _cancellationTokenSource.IsCancellationRequested)
			{
				return;
			}

			try
			{
				await _jobQueue.Writer.WriteAsync( jobOwner, _cancellationTokenSource.Token );
			}
			catch(ChannelClosedException)
			{
				// 종료 과정에서 발생할 수 있는 예외라 무시.
			}
		}

		private async Task WorkerLoopAsync( CancellationToken token )
		{
			_logger.LogInformation( $"Job Worker Thread_{Task.CurrentId} started." );

			try
			{
				// GetConsumingEnumerable은 컬렉션이 비어있으면 블로킹하고,
				// CompleteAdding()이 호출되고, 컬렉션이 비면 루프를 종료합니다.
				// CancellationToken이 취소되면 OperationCanceledException을 발생시킵니다.

				// ReadAllAsync는 채널에서 아이템을 비동기적으로 기다립니다.
				// 채널 Writer가 Complete되고 채널이 비면 루프가 종료됩니다.
				await foreach(var owner in _jobQueue.Reader.ReadAllAsync())
				{
					try
					{
						owner.ProcessJobs();
					}
					catch(Exception ex)
					{
						_logger.LogError( ex, "An error occurred while executing a job." );
					}
				}
			}
			catch(OperationCanceledException) when(token.IsCancellationRequested)
			{
				// 정상적인 종료
				_logger.LogInformation( $"Job Worker Thread_{Task.CurrentId} is shutting down." );
			}
			catch(Exception ex)
			{
				_logger.LogError( $"UnHandled exception in WorkerLoop (Thread_{Task.CurrentId}).", ex );
			}
		}
	}
}
