using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Room;
using Server.Tests.TestHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Room
{
	public class RoomTransitionCoordinatorTests
	{
		// 동기 블로킹 TaskCompletionSource - Task.Run 패턴과 함께 사용
		private static TaskCompletionSource<T> CreateBlockingTcs<T>() => new TaskCompletionSource<T>( TaskCreationOptions.RunContinuationsAsynchronously );

		[Fact(Timeout = 5000)]
		public async Task ActiveTransition_PreventsConcurrent()
		{
			// Arrange
			var (coordinator, mockRoomManager, _) = MockFactoryHelper.CreateCoordinator();
			var sourceRoom = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, 1, 1);
			var (mockSession, _) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: sourceRoom.Object );

			var blockTcs = CreateBlockingTcs<bool>();
			mockRoomManager.Setup( rm => rm.FindRoom( 10 ) )
				.Returns<int>( _ =>
				{
					blockTcs.Task.GetAwaiter().GetResult();
					return null;    // 풀린 후 TargetNotFound로 종료
				} );

			// Act - 첫 호출은 Task.Run으로 분리 (메인 스레드 동기 블로킹 방지)
			Task<RoomTransitionResult> first = Task.Run(() => coordinator.ChangeRoomAsync(mockSession.Object, 10, RoomTransitionReason.PlayerRequest));
			await Task.Delay( 50 );

			// 두 번째 호출 - _activeTransitions.TryAdd에서 false -> AlreadyTransferring
			RoomTransitionResult secondResult = await coordinator.ChangeRoomAsync(mockSession.Object, 11, RoomTransitionReason.PlayerRequest);

			// Assert
			Assert.Equal( RoomTransitionResult.AlreadyTransferring, secondResult );

			// Cleanup
			blockTcs.SetResult( true );
			await first;
		}

		[Fact(Timeout = 5000)]
		public async Task TargetNotFound_ReturnsTargetNotFound()
		{
			// Arrange
			var (coordinator, mockRoomManager, _) = MockFactoryHelper.CreateCoordinator();
			var sourceRoom = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, 1, 1);
			var (mockSession, _) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: sourceRoom.Object );
			mockRoomManager.Setup( rm => rm.FindRoom( 999 ) ).Returns( (IRoom)null );

			// Act
			RoomTransitionResult result = await coordinator.ChangeRoomAsync(mockSession.Object, 999, RoomTransitionReason.PlayerRequest);

			// Assert
			Assert.Equal( RoomTransitionResult.TargetNotFound, result );
			Assert.False( coordinator.TryGetActiveTransition( mockSession.Object.SessionId, out _ ) );
		}

		[Fact(Timeout = 5000)]
		public async Task ContextRemovedAfterCompletion()
		{
			// Arrange - TargetNotFound 경로 활용(빠른 종료 + finally 실행 확인)
			var (coordinator, mockRoomManager, _) = MockFactoryHelper.CreateCoordinator();
			var sourceRoom = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, 1, 1);
			var (mockSession, _) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: sourceRoom.Object );
			mockRoomManager.Setup( rm => rm.FindRoom( It.IsAny<int>() ) ).Returns( (IRoom)null );

			// Act
			await coordinator.ChangeRoomAsync( mockSession.Object, 999, RoomTransitionReason.PlayerRequest );

			// Assert - finally의 _activeTransitions.TryRemove 동작 확인
			Assert.False( coordinator.TryGetActiveTransition( mockSession.Object.SessionId, out _ ) );
		}

		[Fact(Timeout = 5000)]
		public async Task CancelTransition_SetsTokenCancellation()
		{
			// Arrage
			var (coordinator, mockRoomManager, _) = MockFactoryHelper.CreateCoordinator();
			var sourceRoom = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, 1, 1);
			var (mockSession, _) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: sourceRoom.Object );

			var blockTcs = CreateBlockingTcs<bool>();
			mockRoomManager.Setup( rm => rm.FindRoom( It.IsAny<int>() ) )
				.Returns<int>( _ => {
					blockTcs.Task.GetAwaiter().GetResult();
					return null;
				} );

			// Act
			Task<RoomTransitionResult> transitionTask = Task.Run(() => coordinator.ChangeRoomAsync(mockSession.Object, 10, RoomTransitionReason.PlayerRequest));
			await Task.Delay( 50 );

			bool cancelled = coordinator.CancelTransition(mockSession.Object.SessionId, RoomTransitionCancelReason.Disconnect);

			// Assert
			Assert.True( cancelled );
			Assert.True( coordinator.TryGetActiveTransition( mockSession.Object.SessionId, out var context ) );
			Assert.True( context.Cancellation.IsCancellationRequested );

			// Cleanup
			blockTcs.SetResult( true );
			await transitionTask;
		}

		[Fact]
		public void SessionDisconnecting_Subscription_WiredOnConstruction()
		{
			// Arrange + Act
			var (_, _, mockSessionManager) = MockFactoryHelper.CreateCoordinator();

			// Assert - 생성자에서 +=가 호출됐는지
			mockSessionManager.VerifyAdd(
				sm => sm.SessionDisconnecting += It.IsAny<EventHandler<SessionDisconnectingEventArgs>>(), Times.Once );
		}
	}
}
