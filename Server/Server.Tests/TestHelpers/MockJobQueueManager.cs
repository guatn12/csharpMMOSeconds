using ServerCore;

namespace Server.Tests.TestHelpers
{
	/// <summary>
	/// IJobQueueManager의 최소 구현 Mock 클래스
	/// </summary>
	public class MockJobQueueManager : IJobQueueManager
	{
		// IJobQueueManager 계약 - ProcessJob() 내부에서 JoobPool.Return(job) 호출 시 사용됨
		public JobPool JobPool { get; } = new JobPool();

		// 테스트에서는 실제 Worker를 실행하지 않으므로 모두 no-op으로 구현
		public void Start( int workerCount, int channelCapacity = 1000 ) { }
		public Task StopAsync() => Task.CompletedTask;

		// Push() 내부에서 RegisterAsync()가 호출되지만, 테스트는 ProcessJobForTest()로 직접 처리
		public ValueTask RegisterAsync( IJobOwner jobOwner ) => ValueTask.CompletedTask;
	}

}
