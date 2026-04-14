using Server.Tests.Issues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Room
{
	public class PushAsyncTests
	{
		/// <summary>
		/// 검증 1: PushAsync로 작업을 넣고 ProcessJobs 후 결과를 받을 수 있는지
		/// </summary>
		[Fact]
		public async Task PushAsync_NormalOperation_RetrunsResult()
		{
			// Arrange
			var jobQueueManager = new MockJobQueueManager();
			var serializer = new TestJobSerializerWithGate(jobQueueManager);

			// Act
			Task<int> resultTask = serializer.PushAsync<int>(() => new ValueTask<int>(42));

			// PushAsync는 Job을 큐에 넣고 미완료 Task 반환
			Assert.False( resultTask.IsCompleted );

			// 워커 역할 - 큐에 있는 job을 실행
			await serializer.ProcessJobsForTest();

			// Assert
			int result = await resultTask;
			Assert.Equal( 42, result );
		}

		/// <summary>
		/// 검증 2: 작업 중 예외 발생 시 TCS를 통해 호출자에게 전파되는지
		/// </summary>
		[Fact]
		public async Task PushAsync_WorkThrowsException_PropagatesViaTask()
		{
			// Arrange
			var jobQueueManager = new MockJobQueueManager();
			var serializer = new TestJobSerializerWithGate(jobQueueManager);

			// Act
			Task<int> resultTask = serializer.PushAsync<int>(() => throw new InvalidOperationException("Test Error"));

			await serializer.ProcessJobsForTest();

			// Assert - await 시 예외가 전파됨
			var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await resultTask);
			Assert.Equal( "Test Error", ex.Message );
		}

		/// <summary>
		/// 검증 3: CanAccpetJob() == false일 때 PushAsync가 hang하지 않고 즉시 실패
		/// Queue 중지 상태(Closing 등)에서 외부 요청이 영원히 대기하지 않음을 보장
		/// </summary>
		[Fact]
		public async Task PushAsync_QueueStopped_failsImmediately()
		{
			// Arrange
			var jobQueueManager = new MockJobQueueManager();
			var serializer = new TestJobSerializerWithGate(jobQueueManager);
			serializer.AcceptJobs = false; // Queue 중지 상태 시뮬레이션

			// Act
			Task<int> resultTask = serializer.PushAsync<int>( () => new ValueTask<int>(42));

			//Assert - ProcessJobs 호출 없이도 Task가 즉시 완료(실패)되어야 함
			Assert.True( resultTask.IsCompleted );
			Assert.True( resultTask.IsFaulted );

			var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await resultTask);
			Assert.Contains( "작업을 처리할 수 없는 상태", ex.Message );
		}

		/// <summary>
		/// 검증 4: void 반환 PushAsync도 동일하게 동작하는지
		/// </summary>
		[Fact]
		public async Task PushAsync_VoidVersion_CompletesSuccessfully()
		{
			// Arrange
			var jobQueueManager = new MockJobQueueManager();
			var serializer = new TestJobSerializerWithGate(jobQueueManager);
			bool executed = false;

			// Act
			Task resultTask = serializer.PushAsync( async () =>
			{
				await ValueTask.CompletedTask;
				executed = true;
			});

			await serializer.ProcessJobsForTest();

			// Assert
			await resultTask; // 예외 없이 완료
			Assert.True( executed );
		}

	}
}
