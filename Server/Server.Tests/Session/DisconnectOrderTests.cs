using Microsoft.Extensions.Logging;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Room;
using Server.Tests.TestHelpers;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Session
{
	public class DisconnectOrderTests
	{
		[Fact]
		public async Task DoubleEnterGame_OnlyOneJoinLobbyCalled()
		{
			// Arrange: 초기 상태 Connected 
			var (session, _, _) = MockFactoryHelper.CreateRealClientSession( sessionId: 1, connected: true );
			var lobbyMock = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, roomId: 1, mapId: 1);
			var mockICoordinator = new Mock<IRoomTransitionCoordinator>();

			int joinCallCount = 0;
			var mockRoomManager = new Mock<IRoomManager>();
			mockRoomManager
				.Setup( rm => rm.JoinDefaultLobbyAsync( It.IsAny<IClientSession>() ) )
				.Returns<IClientSession>( async s =>
				{
					Interlocked.Increment( ref joinCallCount );
					await Task.Yield();     // 두 번째 호출과 race 강제
					s.SetCurrentRoom( lobbyMock.Object );
					return RoomEnterResult.Success;
				} );

			var handler = MockFactoryHelper.CreateSystemPacketHandler(mockRoomManager, mockICoordinator);

			// Act 
			var t1 = handler.Handlers[typeof(C_EnterGame)](session, new C_EnterGame());
			var t2 = handler.Handlers[typeof(C_EnterGame)](session, new C_EnterGame());
			await Task.WhenAll( t1, t2 );

			// Assert
			Assert.Equal( 1, joinCallCount );
			Assert.Equal( SessionState.InRoom, session.State );
		}

		[Fact]
		public async Task DoubleChangeRoom_OnlyOneMoveCalled()
		{
			// Arrange: 정상 경로로 InRoom 도달
			var (session, _, _) = MockFactoryHelper.CreateRealClientSession( sessionId: 1, connected: true );
			var lobbyMock = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, roomId: 1, mapId: 1);
			var dungeonMock = MockFactoryHelper.CreateMockRoom(RoomType.Dungeon, roomId: 10, mapId: 3);
			

			int moveCallCount = 0;
			var mockRoomManager = new Mock<IRoomManager>();
			mockRoomManager
				.Setup( rm => rm.JoinDefaultLobbyAsync( It.IsAny<IClientSession>() ) )
				.Returns<IClientSession>( s =>
				{
					s.SetCurrentRoom( lobbyMock.Object );
					return Task.FromResult( RoomEnterResult.Success );
				} );
			mockRoomManager
				.Setup( rm => rm.FindAvailableRoomAsync( RoomType.Dungeon ) )
				.ReturnsAsync( dungeonMock.Object );
			var mockICoordinator = new Mock<IRoomTransitionCoordinator>();
			mockICoordinator.Setup( c => c.ChangeRoomAsync( It.IsAny<IClientSession>(), 10, RoomTransitionReason.PlayerRequest ) )
				.Returns<ClientSession, int, RoomTransitionReason>( async ( s, _, __ ) =>
				{
					Interlocked.Increment( ref moveCallCount );
					await Task.Yield();     // race 강제
					s.SetCurrentRoom( dungeonMock.Object );
					return RoomTransitionResult.Success;
				} );

			var handler = MockFactoryHelper.CreateSystemPacketHandler(mockRoomManager, mockICoordinator);

			// 정상 진입: C_EnterGame -> InRoom 도달
			await handler.Handlers[typeof(C_EnterGame)]( session, new C_EnterGame());
			Assert.Equal( SessionState.InRoom, session.State );

			// Act: C_ChangeRoom 2회 호출
			var packet = new C_ChangeRoom{RoomType = (int)RoomType.Dungeon, TargetId = 0 };
			var act = async () =>
			{
				try {await handler.Handlers[typeof( C_ChangeRoom)](session, packet);}
				catch(NullReferenceException){ /* session.Send NRE는 SM-1 검증 범위 밖 무시 처리. */ }
			};

			
			await Task.WhenAll( act(), act() );

			// Assert: MovePlayerToRoomAsync는 정확히 1회 - 두 번째는 Transferring 전이 실패
			Assert.Equal( 1, moveCallCount );
			Assert.Equal( SessionState.InRoom, session.State );		// Transferring -> InRoom
		}

		[Fact]
		public async Task PacketDuringTransferring_DroppedByPacketManager()
		{
			// Arrange: 정상 경로로 InRoom -> ChangeRoom 진행 중에 Move 도착
			var (session, _, _) = MockFactoryHelper.CreateRealClientSession( sessionId: 1, connected: true );
			var lobbyMock = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, roomId: 1, mapId: 1);
			var dungeonMock = MockFactoryHelper.CreateMockRoom(RoomType.Dungeon, roomId: 10, mapId: 3);

			// ChangeRoom 처리를 인위적으로 지연 -> Transferring 상태 유지
			var moveSatrted = new TaskCompletionSource();
			var moveProceed = new TaskCompletionSource();

			var mockRoomManager = new Mock<IRoomManager>();
			mockRoomManager
				.Setup( rm => rm.JoinDefaultLobbyAsync( It.IsAny<IClientSession>() ) )
				.Returns<IClientSession>( s =>
				{
					s.SetCurrentRoom( lobbyMock.Object );
					return Task.FromResult( RoomEnterResult.Success );
				} );
			mockRoomManager
				.Setup( rm => rm.FindAvailableRoomAsync( RoomType.Dungeon ) )
				.ReturnsAsync( dungeonMock.Object );
			
			var mockICoordinator = new Mock<IRoomTransitionCoordinator>();
			mockICoordinator.Setup( c => c.ChangeRoomAsync( It.IsAny<IClientSession>(), 10, RoomTransitionReason.PlayerRequest ) )
				.Returns<ClientSession, int, RoomTransitionReason>( async ( s, _, __ ) =>
				{
					moveSatrted.SetResult();                // Transferring 진입을 외부에 알림
					await moveProceed.Task;                 // 외부가 신호 줄 때까지 유지
					s.SetCurrentRoom( dungeonMock.Object );
					return RoomTransitionResult.Success;
				} );

			var systemHandler = MockFactoryHelper.CreateSystemPacketHandler(mockRoomManager, mockICoordinator);

			var loggerFactory = LoggerFactory.Create(b => { });
			var jobQueueManager = new JobQueueManager(loggerFactory.CreateLogger<JobQueueManager>());
			var (packetManager, mockPmLogger) = MockFactoryHelper.CreateRealPacketManager( jobQueueManager, systemHandler );

			// 정상 진입: C_EnterGame -> InRoom 도달
			await systemHandler.Handlers[typeof(C_EnterGame)]( session, new C_EnterGame());

			// Transferring 상태 진입: C_ChangeRoom -> MovePlayerToRoomAsync 호출
			var changeTask = systemHandler.Handlers[typeof(C_ChangeRoom)]( session, new C_ChangeRoom{RoomType = (int)RoomType.Dungeon, TargetId = 0 });
			await moveSatrted.Task;
			Assert.Equal(SessionState.Transferring, session.State );

			// Act: Transferring 시점에 C_Move 도착
			var movePacket = new C_Move {PosInfo = new PosInfo{PosX = 10 } };
			var buffer = MockFactoryHelper.SerializeClientPacket(PacketID.C_Move, movePacket);
			await packetManager.HandlePacket( session, buffer );

			// 정리
			moveProceed.SetResult();
			try { await changeTask; } catch(NullReferenceException) { /* SM-1에서 session.Send NRE는 무시 */ }

			// Assert: 1차 필터 "dropped" 로그 정확히 1회, "Packet received" 로그 없음.
			VerifyLogContains( mockPmLogger, LogLevel.Debug, "dropped", Times.Once() );
			VerifyLogContains( mockPmLogger, LogLevel.Debug, "Packet received", Times.Never() );
		}

		// 헬퍼 : 특정 메시지 토큰 포함 LogDug 호출 횟수 검증
		private static void VerifyLogContains<T> (Mock<ILogger<T>> mockLogger, LogLevel level, string contains, Times times)
		{
			mockLogger.Verify( l => l.Log( level, It.IsAny<EventId>(),
				It.Is<It.IsAnyType>( ( v, _ ) => v.ToString().Contains( contains ) ),
				It.IsAny<Exception>(),
				It.IsAny<Func<It.IsAnyType, Exception, string>>() ), times );
		}

		[Fact]
		public async Task PacketDuringTransferring_DroppedByHandlerGuard()
		{
			// Arrange: 1.5차 가드는 본질적으로 비정상 시나리오
			// 정상 경로 재현 불가, 따라서 ForceState 사용
			var (lobby, _) = IntegrationTestHelper.BuildLobbyRoom( roomId: 1 );
			var (session, _, _) = MockFactoryHelper.CreateRealClientSession( sessionId: 1, connected: true );
			session.SetCurrentRoom( lobby );

			// 1차 필터 우회 시뮬레이션
			session.ForceState( SessionState.Transferring );

			var movePacket = new C_Move {PosInfo = new PosInfo{PosX = 10, PosY = 10 } };
			var buffer = MockFactoryHelper.SerializeClientPacket(PacketID.C_Move, movePacket);

			// Act: PacketManager(1차) 우회 -> RoomPacketHandler 직접 호출
			IRoom roomAsInterface = lobby;
			await roomAsInterface.RoomPacketHandler.HandleAsync( session, (ushort)PacketID.C_Move, buffer );

			// Assert: 핸들러 본문 미실행
			Assert.NotEqual( 10, session.Player.PosInfo.PosX );
			Assert.NotEqual(10, session.Player.PosInfo.PosY );
		}

		[Fact]
		public async Task OnDisConnected_LeavesRoom_UnregisterSession_TransitionsToDisconnected()
		{
			// Arrange
			var (lobby, jqm) = await IntegrationTestHelper.BuildAndRegisterLobbyRoomAsync( roomId: 1 );
			try
			{
				var (session, _, mockSessionManager) = MockFactoryHelper.CreateRealClientSession( sessionId: 42, connected: true );

				// 정상 진입
				session.SetCurrentRoom( lobby );
				await lobby.EnterViaQueueAsync( session );

				// Act
				session.OnDisConnected( new IPEndPoint( IPAddress.Loopback, 0 ) );
				await MockFactoryHelper.WaitForStateAsync( session, SessionState.Disconnected, 1000 );

				// Assert	
				Assert.Equal( SessionState.Disconnected, session.State );
				mockSessionManager.Verify( m => m.UnregisterSession( 42 ), times: Times.Once() );
				Assert.False( lobby.ContainsPlayer( session ) );
			}
			finally
			{
				await jqm.StopAsync();		// Worker 정리
			}
		}

		[Fact]
		public async Task OnDisConnected_CalledTwince_UnregisterSessionExactlyOnce()
		{
			// Arrange
			var (lobby, jqm) = await IntegrationTestHelper.BuildAndRegisterLobbyRoomAsync( roomId: 1 );
			try
			{
				var (session, _, mockSessionManager) = MockFactoryHelper.CreateRealClientSession( sessionId: 7, connected: true );
				session.SetCurrentRoom( lobby );
				await lobby.EnterViaQueueAsync( session );

				// Act
				session.OnDisConnected( new IPEndPoint( IPAddress.Loopback, 0 ) );
				session.OnDisConnected( new IPEndPoint( IPAddress.Loopback, 0 ) );

				// 첫 번째 호출의 비동기 정리 완료까지 대기
				await MockFactoryHelper.WaitForStateAsync( session, SessionState.Disconnected, 1000 );

				// Assert
				mockSessionManager.Verify( m => m.UnregisterSession( 7 ), times: Times.Once() );
			}
			finally { await jqm.StopAsync(); }
		}
	}
}
