using ServerCore;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Server.Tests.Issues
{
	public class JobQueueFixedTests
	{
		private readonly ITestOutputHelper _output;

		public JobQueueFixedTests( ITestOutputHelper output )
		{
			_output = output;
		}

		[Fact]
		public async Task Test_AsyncJob_Seriality_Guaranteed()
		{
			var mockJobQueueManager = new MockJobQueueManager();
			var mockTestRoom = new TestRoom( mockJobQueueManager );
			var mockLogger = new List<string>();
			var mockStopWatch = new Stopwatch();
			mockStopWatch.Start();

			var job1 = new DelegateJob();
			job1.Initialize( async () =>
			{
				int threadId = Thread.CurrentThread.ManagedThreadId;
				mockLogger.Add( $"[{mockStopWatch.ElapsedMilliseconds}ms] Job1 Start - Thread({threadId})" );
				await Task.Delay( 200 ); // Simulate some async work (e.g., DB call)
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

			await mockTestRoom.ProcessJobsForTest();

			foreach(var entry in mockLogger)
				_output.WriteLine( entry );
			
			long job1EndTime = ParseTime(mockLogger.First(s => s.Contains("Job1 End")));
			long job2StartTime = ParseTime(mockLogger.First(s => s.Contains("Job2 Run")));
			// 검증 1: Job1이 실제로 150ms 이상 소요됨 (블로킹 발생하지 않음)
			Assert.True( job1EndTime >= 150, $"Expected Job1 to take at least 150ms, but it took {job1EndTime}ms." );
			// 검증 2: Job2가 Job1이 완전히 끝난 후에 실행됨 (직렬성 보장 확인)
			Assert.True( job1EndTime <= job2StartTime, "Job2가 Job1이 완전히 끝난 후에 실행됨 (직렬성 보장 확인)" );
		}

		[Fact]
		public async Task Test_MixedJobs_OrderGuaranteed()
		{
			var mockJobQueueManager = new MockJobQueueManager();
			var mockTestRoom = new TestRoom( mockJobQueueManager );
			var log = new List<string>();
			var sw = new Stopwatch();
			sw.Start();

			var job1 = new DelegateJob();
			job1.Initialize( () =>
			{
				int threadId = Thread.CurrentThread.ManagedThreadId;
				log.Add( $"[{sw.ElapsedMilliseconds}ms] Job1 Start - Thread{threadId}" );
				log.Add( $"[{sw.ElapsedMilliseconds}ms] Job1 End - Thread{threadId}" );
			} );
			mockTestRoom.Push( job1 );


			var job2 = new DelegateJob();
			job2.Initialize( async () =>
			{
				int threadId = Thread.CurrentThread.ManagedThreadId;
				log.Add( $"[{sw.ElapsedMilliseconds}ms] Job2 Start - Thread{threadId}" );
				await Task.Delay( 1000 );
				log.Add( $"[{sw.ElapsedMilliseconds}ms] Job2 End - Thread{threadId}" );
			} );
			mockTestRoom.Push( job2 );

			var job3 = new DelegateJob();
			job3.Initialize( () =>
			{
				int threadId = Thread.CurrentThread.ManagedThreadId;
				log.Add( $"[{sw.ElapsedMilliseconds}ms] Job3 Run - Thread{threadId}" );
			} );
			mockTestRoom.Push( job3 );

			await mockTestRoom.ProcessJobsForTest();
			
			foreach(var entry in log)
				_output.WriteLine( entry );

			int job1Idx = log.FindIndex(s => s.Contains( "Job1 Start" ) );
			int job2Idx = log.FindIndex(s => s.Contains( "Job2 Start" ) );
			int job3Idx = log.FindIndex(s => s.Contains( "Job3 Run" ) );

			// 검증1: 실행 순서가 job1 -> job2 -> job3 인지
			Assert.True(job1Idx < job2Idx && job2Idx < job3Idx, $"실행 순서 불일치: Job1 idx={job1Idx}, Job2 idx={job2Idx}, Job3 idx={job3Idx}" );

			// 검증2: job3는 job2의 비동기 작업(1000ms)이 완료된 후 실행
			long job2EndTime = ParseTime(log.First(s => s.Contains( "Job2 End")));
			long job3StartTime = ParseTime(log.First(s => s.Contains("Job3 Run")));
			Assert.True( job2EndTime <= job3StartTime, "Job3가 Job2 완료 전에 실행됌" );
		}

		private static long ParseTime( string log )
		{
			var match = System.Text.RegularExpressions.Regex.Match(log, @"\[(\d+)ms\]");
			return match.Success ? long.Parse( match.Groups[ 1 ].Value ) : 0;
		}
	}
}
