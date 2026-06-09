using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Room;
using Server.Tests.TestHelpers;
using ServerCore;

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

		/// <summary>
		/// J-2 - TryReserveEnter 큐 대기 중 cancel -> cancelled 반환 + reservation 누수 0
		/// </summary>
		[Fact(Timeout = 5000)]
		public async Task Coordinator_CancelDuringReserve_ReturnsCancelled()
		{
			// Arrange
			var (sourceRoom, _) = await IntegrationTestHelper.BuildAndRegisterLobbyRoomAsync( roomId: 1, maxPlayers: 2 );
			var (targetRoom, _) = await IntegrationTestHelper.BuildAndRegisterLobbyRoomAsync( roomId: 10, maxPlayers: 2 );

			var (coordinator, mockRoomManager, _) = MockFactoryHelper.CreateCoordinator();
			var (mockSession, _) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: sourceRoom );
			mockRoomManager.Setup( rm => rm.FindRoom( 10 ) ).Returns( targetRoom );

			// sourceRoom에 세션을 실제로 입장시킨다 - _players에 추가되어야 LeaveViaQueueAysnc 성공
			RoomEnterResult enterResult = await sourceRoom.EnterViaQueueAsync(mockSession.Object);
			Assert.Equal(RoomEnterResult.Success, enterResult );

			// targetRoom의 worker를 blocking job으로 점유 -> tryreserveenter가 큐 대기 상태에 머묾
			var releaseBlocker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			_ = targetRoom.PushAsync( async () =>
			{
				await releaseBlocker.Task;
			}, CancellationToken.None );

			// Act - ChangeRoomAsync 시작. TryReaserveEnter PushAsync가 target queue에서 대기 상태
			Task<RoomTransitionResult> transitiontask = Task.Run(() => coordinator.ChangeRoomAsync(mockSession.Object, 10, RoomTransitionReason.PlayerRequest));

			await Task.Delay( 50 );     // ChangeRoomAsync가 TryReserveEnter PushAsync까지 도달할 시간

			// Cancel - 큐 대기 중에 cancel 신호
			bool cancelled = coordinator.CancelTransition(mockSession.Object.SessionId, RoomTransitionCancelReason.Disconnect);
			Assert.True( cancelled );

			// Blocker 해제 -> worker가 ProcessJobAsync 루프 실행 -> TryReserveEnter dispatch
			// -> token.IsCancellationRequested -> OCE -> Coordinator catch -> Cancelled
			releaseBlocker.SetResult( true );

			var result = await transitiontask;

			// Assert - Cancelled 반환 + context 정리
			Assert.Equal( RoomTransitionResult.Cancelled, result );
			Assert.False( coordinator.TryGetActiveTransition( mockSession.Object.SessionId, out _ ) );

			// 누수 검증 - Cancelled 분기 + maxPlaysers=2 -> 2 reserves 모두 성공해야 함
			// Cancelled+누수0: players=0, pending=0+1+2 -> (t, t)
			// Cancelled+누수1: players=0, pending=1+2 -> (t, f) 2번째가 leak 신호
			bool first = await targetRoom.PushAsync<bool>(() => ValueTask.FromResult(targetRoom.TryReserveEnter()), CancellationToken.None);
			bool second = await targetRoom.PushAsync<bool>(() => ValueTask.FromResult(targetRoom.TryReserveEnter()), CancellationToken.None);

			Assert.True( first, "Cancelled 후 1차 reserve 실패 - 누수 의심" );
			Assert.True( second, "Cancelled 후 2차 reserve 실패 - _pendingEnterCount 누수 1건 의심" );
		}

		/// <summary>
		/// J-2 - EnterWithReservationAsync PushAsync는 token 미전달 이므로
		/// reservation 후 cancel 시 잡이 끝까지 실행되어 release 보장
		/// race로 인해 result는 Success / Cancelled 둘 다 가능, 각 분기마다 capacity 정합 검증
		/// </summary>
		[Fact(Timeout = 5000)]
		public async Task Coordinator_CancelAfterReserve_EnterJobStillRunsAndReleases()
		{
			// Arrange
			var (sourceRoom, _) = await IntegrationTestHelper.BuildAndRegisterLobbyRoomAsync( roomId: 1, maxPlayers: 2 );
			var (targetRoom, _) = await IntegrationTestHelper.BuildAndRegisterLobbyRoomAsync( roomId: 10, maxPlayers: 2 );

			var (coordinator, mockRoomManager, _) = MockFactoryHelper.CreateCoordinator();

			var (mockSession, _) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: sourceRoom );
			mockRoomManager.Setup( rm => rm.FindRoom( 10 ) ).Returns( targetRoom );

			// sourceRoom에 세션 실제 입장
			RoomEnterResult enterResult = await sourceRoom.EnterViaQueueAsync(mockSession.Object);
			Assert.Equal( RoomEnterResult.Success, enterResult );

			// Act - race 시도
			Task<RoomTransitionResult> transitionTask = Task.Run(() => coordinator.ChangeRoomAsync(mockSession.Object, 10, RoomTransitionReason.PlayerRequest));

			await Task.Delay( 50 );

			// reservation 진입 후 cancel - EnterWithReservationAsync 잡은 token 미전달이라 정상 실행
			coordinator.CancelTransition( mockSession.Object.SessionId, RoomTransitionCancelReason.Disconnect );

			var result = await transitionTask;

			// Assert - race 결과는 Success | Cancelled 양쪽 valid
			Assert.True( result == RoomTransitionResult.Success || result == RoomTransitionResult.Cancelled );
			Assert.False( coordinator.TryGetActiveTransition( mockSession.Object.SessionId, out _ ) );

			// 누수 검증 - maxPlayers=2 + 분기별 expected
			// Success+누수0: players=1, max=2 → 1차 reserve (1+0<2) T, 2차 reserve (1+1=2) F
			// Success+누수1: players=1, max=2 → 1차 reserve (1+1=2) F ← 누수 신호
			// Cancelled+누수0: players=0, max=2 → 1차 (0+0<2) T, 2차 (0+1<2) T
			// Cancelled+누수1: players=0, max=2 → 1차 (0+1<2) T, 2차 (0+2=2) F ← 누수 신호
			bool first = await targetRoom.PushAsync<bool>(() => ValueTask.FromResult(targetRoom.TryReserveEnter()), CancellationToken.None);
			bool second = await targetRoom.PushAsync<bool>(() => ValueTask.FromResult(targetRoom.TryReserveEnter()), CancellationToken.None);

			if(result == RoomTransitionResult.Success)
			{
				Assert.True( first, "Success인데 1차 reserve 실패 - Success+누수 의심(Player=1+pending=1=2)" );
				Assert.False( second, "Success인데 2차 reserve 성공 - 비정상 (room이 max를 초과)" );
			}
			else
			{
				Assert.True( first, "Cancelled인데 1차 reserve 실패 - Cancelled+누수 의심" );
				Assert.True( second, "Cancelled인데 2차 reserve 실패 - _pendingEnterCount 누수 의심" );
			}
		}
	}
}
