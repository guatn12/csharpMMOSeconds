
using Server.Tests.TestHelpers;

namespace Server.Tests.Infra
{
	public class PushAsyncCancellationTests
	{
		/// <summary> 검증 1: Push 시점에 이미 cancel된 토큰은 큐에 들어가지 않고 즉시 Canceled </summary>
		[Fact]
		public void PushAsync_TokenAlreadyCanceled_ReturnsCanceledTaskImmediately()
		{
			// Arrange
			var jobQueueManager = new MockJobQueueManager();
			var serializer = new TestJobSerializerWithGate(jobQueueManager);
			using var cts = new CancellationTokenSource();
			cts.Cancel(); // 토큰을 이미 취소 상태로 만듦

			// Act
			Task<int> resultTask = serializer.PushAsync<int>( () => new ValueTask<int>(32), cts.Token);

			// Assert
			Assert.True( resultTask.IsCompleted );
			Assert.True( resultTask.IsCanceled );

			var ex = Assert.ThrowsAsync<TaskCanceledException>(async () => await resultTask).Result;
			Assert.Equal( cts.Token, ex.CancellationToken );
		}

		/// <summary> 검증 2: 큐 대기 중 cancel -> dispatch 직전 검사가 OperationCanceledException 발생 </summary>
		[Fact]
		public async Task PushAsync_QueueWaiting_CancelImmediately()
		{
			// Arrange
			var jobQueueManager = new MockJobQueueManager();
			var serializer = new TestJobSerializerWithGate(jobQueueManager);
			using var cts = new CancellationTokenSource();

			bool workExecuted = false;
			Task<int> resultTask = serializer.PushAsync<int>(
				() =>
				{
					workExecuted = true;
					return new ValueTask<int>(32);
				}, cts.Token);

			// Push만 했고 ProcessJobs 미호출 - Task 미완료 상태
			Assert.False( resultTask.IsCompleted );

			// Act - 큐 대기 중 Cancel
			cts.Cancel();

			// 워커 역할 - 큐 dispatch
			await serializer.ProcessJobsForTest();

			// Assert - dispatch 직전 검사에서 끊김, work 미실행
			Assert.True( resultTask.IsCanceled );
			Assert.False( workExecuted );

			var ex = await Assert.ThrowsAsync<TaskCanceledException>(async () => await resultTask);
			Assert.Equal( cts.Token, ex.CancellationToken );
		}

		/// <summary> 검증 3: 잡 본문 진입 후 cancel은 무시되고 본문이 끝까지 진행 </summary>
		[Fact]
		public async Task PushAsync_InsideJob_CancelIgnored()
		{
			// Arrange
			var jobqueueManager = new MockJobQueueManager();
			var serializer = new TestJobSerializerWithGate(jobqueueManager);
			using var cts = new CancellationTokenSource();

			var workStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			var allowComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			Task<int> resultTask = serializer.PushAsync<int>(
				async() =>
				{
					workStarted.SetResult(true);
					await allowComplete.Task;		// 본문 안에서 대기
					return 32;
				}, cts.Token);

			// Act - ProcessJobs는 별도 Task에서 (workStarted 대기를 메인이 잡기 위함)
			Task processTask = Task.Run(async() => await serializer.ProcessJobsForTest());

			// 본문 진입 확인
			await workStarted.Task;

			// 본문 진입 후 cancel
			cts.Cancel();

			// 본문 완료 신호
			allowComplete.SetResult( true );
			await processTask;

			// Assert - 본문이 끝까지 실행되어 32 반환
			int result = await resultTask;
			Assert.Equal( 32, result );
			Assert.False( resultTask.IsCanceled );
		}

		/// <summary> token 미전달 호출처는 동작 무변경 </summary>
		[Fact]
		public async Task PushAsync_DefaultToken_NoBehaviorChange()
		{
			// Arrange
			var jobQueueManager = new MockJobQueueManager();
			var serializer = new TestJobSerializerWithGate(jobQueueManager);

			// Act - token 미전달 호출
			Task<int> resultTask = serializer.PushAsync<int>(() => new ValueTask<int>(32));

			await serializer.ProcessJobsForTest();

			// Assert - 기존 동작 그대로
			int result = await resultTask;
			Assert.Equal( 32, result );
			Assert.False( resultTask.IsCanceled );
		}

		/// <summary> Void 반환 PUshAsync도 동일하게 cancel 적용 </summary>
		[Fact]
		public async Task PushAsync_VoidVersion_CancelDuringQueueWait()
		{
			// Arrange
			var jobQueueManager = new MockJobQueueManager();
			var serializer = new TestJobSerializerWithGate(jobQueueManager);
			using var cts = new CancellationTokenSource();
			bool workExecuted = false;

			Task resultTask = serializer.PushAsync(
				() =>
				{
					workExecuted = true;
					return new ValueTask();
				}, cts.Token);

			// Act - 큐 대기 중 Cancel
			cts.Cancel();
			await serializer.ProcessJobsForTest();

			// Assert
			Assert.True( resultTask.IsCanceled );
			Assert.False( workExecuted );
		}

	}
}
