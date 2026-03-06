using ServerCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Server.Tests.Issues
{
	public class JobQueueBlockingTests
	{
		private readonly ITestOutputHelper _output;

		public JobQueueBlockingTests(ITestOutputHelper output)
		{
			_output = output;
		}

		[Fact]
		public void Test_WorkerBlocking_GetAwaiterResult()
		{
			var mockJobQueueManager = new MockJobQueueManager();
			var mockTestRoom = new TestRoom( mockJobQueueManager );
			var mockLogger = new List<string>();
			var mockStopWatch = new Stopwatch();
			mockStopWatch.Start();

			var job1 = new DelegateJob();
			job1.Initialize( () => 
			{
				int threadId = Thread.CurrentThread.ManagedThreadId;
				mockLogger.Add( $"[{mockStopWatch.ElapsedMilliseconds}ms] Job1 Start - Thread({threadId})" );

				SimulateDbAsync(500).GetAwaiter().GetResult();

				mockLogger.Add( $"[{mockStopWatch.ElapsedMilliseconds}ms] Job1 End - Thread({threadId})" );
			} );

			var job2 = new DelegateJob();
			job2.Initialize( () => 
			{
				int threadId = Thread.CurrentThread.ManagedThreadId;
				mockLogger.Add( $"[{mockStopWatch.ElapsedMilliseconds}ms] Job2 Run - Thread({threadId})" );
			} );

			mockTestRoom.Push( job1 );
			mockTestRoom.Push( job2 );

			mockTestRoom.ProcessJobsForTest();

			foreach(var entry in mockLogger)
				_output.WriteLine( entry );

			long job1EndTime = ParseTime(mockLogger.First(s => s.Contains("Job1 End")));
			long job2StartTime = ParseTime(mockLogger.First(s => s.Contains("Job2 Run")));

			// 검증 1: Job1이 실제로 500ms 이상 소요됨 (블로킹 발생)
			Assert.True( 450 <= job1EndTime, $"Job1 종료 시각({job1EndTime}ms)이 예상보다 짧습니다. 블로킹이 발생하지 않은 것 같습니다." );

			// 검증 2: Job2는 job1이 끝난 후에 실행됨 (직렬성은 지켜짐, 그러나 전체 지연 발생)
			Assert.True( job1EndTime <= job2StartTime, "Job2가 job1 종료 전에 실행되었습니다. 직렬성이 깨졌습니다." );

		}

		[Fact]
		public async Task Test_ThreadTransfer_FireAndForget()
		{
			var mockManager = new MockJobQueueManager();
			var mockTestRoom = new TestRoom( mockManager );
			var mockLogger = new ConcurrentBag<string>();
			var stopWatch = new Stopwatch();
			int sharedCounter = 0;

			stopWatch.Start();

			var continuationDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			var job1 = new DelegateJob();
			job1.Initialize( () =>
			{
				int threadId = Thread.CurrentThread.ManagedThreadId;
				mockLogger.Add( $"[{stopWatch.ElapsedMilliseconds}ms] Job1-Sync - Thread {threadId}" );

				_ = SimulateAsyncHandlerAsync( mockLogger, stopWatch,
					onIncrement: () => Interlocked.Increment( ref sharedCounter ), tcs: continuationDone );

				mockLogger.Add( $"[{stopWatch.ElapsedMilliseconds}ms] Job1-FaF-Return - Thread {threadId} <- Worker: Job1 완료로 인식" );
			} );

			var job2 = new DelegateJob();
			job2.Initialize( () =>
			{
				int threadId = Thread.CurrentThread.ManagedThreadId;
				mockLogger.Add( $"[{stopWatch.ElapsedMilliseconds}ms] Job2-Run - Thread {threadId}" );

				Interlocked.Increment( ref sharedCounter );
				mockLogger.Add( $"[{stopWatch.ElapsedMilliseconds}ms] Job2-Counter={sharedCounter} - Thread {threadId}" );
			} );

			mockTestRoom.Push( job1 );
			mockTestRoom.Push( job2 );

			mockTestRoom.ProcessJobsForTest();

			await Task.WhenAny(continuationDone.Task, Task.Delay(2000) ); // 최대 2초 대기

			var sortedLog = mockLogger.OrderBy(ParseTime).ToList();
			foreach(var entry in sortedLog)
				_output.WriteLine( entry );

			string continuationLog = sortedLog.FirstOrDefault(s => s.Contains("Job1-Continuation"));
			string job1ReturnLog = sortedLog.FirstOrDefault(s => s.Contains("Job1-FaF-Return"));

			Assert.NotNull( continuationLog );

			int continuationThread = ParseThreadId( continuationLog );
			int job1Thread = ParseThreadId( job1ReturnLog );
			long returnTime = ParseTime( job1ReturnLog );
			long continuationTime = ParseTime(continuationLog);

			if(continuationThread != job1Thread)
				_output.WriteLine( $"\n Race Condition 위험: Continuation(Thread{continuationThread})이 Job2(Thread {job1Thread})와 다른 스레드 - 동시 접근 가능" );
			else
				_output.WriteLine( $"\n 우연히 같은 스레드(Thread {continuationThread}) - TaskCheduler 최적화, 재실행 시 달라질 수 있음" );

			Assert.True( returnTime <= continuationTime, "Job1-FaF-Return이 Continuation보다 나중에 찍혔습니다. Fire-and-forget이 동작하지 않은 것 같습니다." );
		}

		private static async Task SimulateDbAsync(int delayMs)
			=> await Task.Delay( delayMs );

		private static async Task SimulateAsyncHandlerAsync( ConcurrentBag<string> log, Stopwatch sw,
			Action onIncrement, TaskCompletionSource<bool> tcs )
		{
			int threadBefore = Thread.CurrentThread.ManagedThreadId;
			log.Add( $"[{sw.ElapsedMilliseconds}ms] Job1-Async-Start - Thread {threadBefore}" );

			await Task.Delay( 100 ); // Redis / DB I/O 시뮬레이션

			// await 이후 - ThreadPool에서 재개될 수 있음 (스레드 이탈 발생 지점)
			int threadAfter = Thread.CurrentThread.ManagedThreadId;
			log.Add($"[{sw.ElapsedMilliseconds}ms] Job1-Continuation - Thread {threadAfter}" );

			onIncrement();  // Interlocked.Increment(ref sharedCounter); 실행
			tcs.TrySetResult( true ); // 비동기 작업 완료 신호
		}

		private static long ParseTime( string log )
		{
			var match = System.Text.RegularExpressions.Regex.Match(log, @"\[(\d+)ms\]");
			return match.Success ? long.Parse( match.Groups[ 1 ].Value ) : 0;
		}

		private static int ParseThreadId( string log )
		{
			var match = System.Text.RegularExpressions.Regex.Match(log, @"Thread (\d+)");
			return match.Success ? int.Parse( match.Groups[ 1 ].Value ) : -1;
		}
	}
}
