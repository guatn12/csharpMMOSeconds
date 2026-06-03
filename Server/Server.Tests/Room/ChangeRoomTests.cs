using Google.Protobuf;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Game;
using Server.Room;
using Server.Tests.TestHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Room
{
	public class ChangeRoomTests
	{
		

		/// <summary>
		/// 검증 1: 이미 같은 타입의 방에 있을 때 재요청 -> 실패
		/// </summary>
		/// <returns></returns>
		[Fact]
		public async Task HandleChangeRoom_AlreadyInSameRoomType_ReturnsFailures()
		{
			// Arrange
			var dungeonRoom = MockFactoryHelper.CreateMockRoom(RoomType.Dungeon, roomId:10, mapId:3);
			var (mockSession, sentPackets) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: dungeonRoom.Object );
			
			var mockRoomManager = new Mock<IRoomManager>();
			var mockCoordinator = new Mock<IRoomTransitionCoordinator>();
			var handler = MockFactoryHelper.CreateSystemPacketHandler(mockRoomManager, mockCoordinator);

			var packet = new C_ChangeRoom
			{
				RoomType = (int)RoomType.Dungeon,
				TargetId = 0
			};

			// Act
			await handler.Handlers[ typeof( C_ChangeRoom ) ]( mockSession.Object, packet );

			// Assert
			var response = Assert.Single(sentPackets) as S_ChangeRoom;
			Assert.NotNull( response );
			Assert.False( response.Success );
			Assert.Contains( "이미", response.FailReason );

			// 방 이동 시도 자체가 없어야 함
			mockCoordinator.Verify(c => c.ChangeRoomAsync( It.IsAny<IClientSession>(), It.IsAny<int>(), It.IsAny<RoomTransitionReason>() ), Times.Never );
		}

		/// <summary>
		/// 검증 2: MovePlayerToRoomAsync가 RoomFull 반환 -> 실패
		/// </summary>
		/// <returns></returns>
		[Theory]
		[InlineData( RoomTransitionResult.TargetFull, "방이 가득 찼습니다." )]
		[InlineData( RoomTransitionResult.TargetClosed, "방이 닫혀 있습니다." )]
		[InlineData( RoomTransitionResult.TargetNotFound, "방을 찾을 수 없습니다." )]
		[InlineData( RoomTransitionResult.AlreadyTransferring, "이미 방 이동 중입니다." )]
		[InlineData( RoomTransitionResult.Cancelled, "방 이동이 취소되었습니다." )]
		[InlineData( RoomTransitionResult.RollbackSucceeded, "방 이동에 실패했습니다. 원래 방으로 복귀했습니다." )]
		[InlineData( RoomTransitionResult.RollbackFailed, "방 이동에 실패했습니다." )]
		public async Task HandleChangeRoom_CoordinatorFailure_MapsCorrectFailReason(RoomTransitionResult transitionResult, string expectedFailReason)
		{
			// Arrange
			var lobbyRoom = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, roomId:1, mapId:1);
			var dungeonRoom = MockFactoryHelper.CreateMockRoom(RoomType.Dungeon, roomId:10, mapId:3);
			var (mockSession, sentPackets) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: lobbyRoom.Object );

			var mockRoomManager = new Mock<IRoomManager>();
			mockRoomManager.Setup(rm => rm.FindAvailableRoomAsync(RoomType.Dungeon)).ReturnsAsync(dungeonRoom.Object );
			var mockCoordinator = new Mock<IRoomTransitionCoordinator>();
			mockCoordinator.Setup( c => c.ChangeRoomAsync( mockSession.Object, dungeonRoom.Object.RoomId, RoomTransitionReason.PlayerRequest ) )
				.ReturnsAsync( transitionResult );
			var handler = MockFactoryHelper.CreateSystemPacketHandler(mockRoomManager, mockCoordinator);
			var packet = new C_ChangeRoom{RoomType = (int)RoomType.Dungeon, TargetId = 0};

			// Act
			await handler.Handlers[ typeof( C_ChangeRoom ) ]( mockSession.Object, packet );

			// Assert
			var response = Assert.Single(sentPackets) as S_ChangeRoom;

			Assert.NotNull( response );
			Assert.False( response.Success );
			Assert.Equal( expectedFailReason, response.FailReason );
		}

		/// <summary>
		/// 검증 3: 가용 던전 없을 때 On-Demand 생성 후 정상 입장
		/// 빈 던전 삭제 후 재요청 시나리오 포함
		/// </summary>
		/// <returns></returns>
		[Fact]
		public async Task HandleChangeRoom_NoRoomAvailable_CreatesOnDemandAndSucceeds()
		{
			// Arrange
			var lobbyRoom = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, roomId:1, mapId:1);
			var newDungeonRoom = MockFactoryHelper.CreateMockRoom(RoomType.Dungeon, roomId:10, mapId:3);
			var (mockSession, sentPackets) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: lobbyRoom.Object );

			var mockRoomManager = new Mock<IRoomManager>();
			// 가용 방 없음 (삭제된 상태)
			mockRoomManager.Setup( rm => rm.FindAvailableRoomAsync( RoomType.Dungeon ) ).ReturnsAsync( (IRoom)null );
			// On-Demand 생성
			mockRoomManager.Setup( rm => rm.CreateRoomAsync( RoomType.Dungeon, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IClientSession>() ) ).ReturnsAsync( newDungeonRoom.Object );
			// 이동 성공 + 세션 룸 갱신
			var mockCoordinator = new Mock<IRoomTransitionCoordinator>();
			mockCoordinator.Setup( c => c.ChangeRoomAsync( mockSession.Object, newDungeonRoom.Object.RoomId, RoomTransitionReason.PlayerRequest ) )
			   .Callback( () => mockSession.Object.SetCurrentRoom( newDungeonRoom.Object ) )
			   .ReturnsAsync( RoomTransitionResult.Success );

			var handler = MockFactoryHelper.CreateSystemPacketHandler(mockRoomManager, mockCoordinator);
			var packet = new C_ChangeRoom{RoomType=(int)RoomType.Dungeon, TargetId = 0 };

			// Act
			await handler.Handlers[ typeof( C_ChangeRoom ) ]( mockSession.Object, packet );

			// Assert: OnDemand 생성이 1회 호출되었는지 확인
			mockRoomManager.Verify( rm => rm.CreateRoomAsync( RoomType.Dungeon, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IClientSession>() ), Times.Once() );

			var response = Assert.Single(sentPackets) as S_ChangeRoom;
			Assert.NotNull( response );
			Assert.True( response.Success );
			Assert.Equal( 3, response.MapId );

		}

		/// <summary>
		/// 검증 4:로비 -> 던전 정상 이동
		/// </summary>
		/// <returns></returns>
		[Fact]
		public async Task HandleChangeRoom_LobbyToDungeon_Succeeds()
		{
			// Arrange
			var lobbyRoom   = MockFactoryHelper.CreateMockRoom(RoomType.Lobby,   roomId: 1, mapId: 1);
			var dungeonRoom = MockFactoryHelper.CreateMockRoom(RoomType.Dungeon, roomId: 10, mapId: 3);
			var (mockSession, sentPackets) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: lobbyRoom.Object );

			var mockRoomManager = new Mock<IRoomManager>();
			mockRoomManager.Setup( rm => rm.FindAvailableRoomAsync( RoomType.Dungeon ) ).ReturnsAsync( dungeonRoom.Object );
			
			var mockCoordinator = new Mock<IRoomTransitionCoordinator>();
			mockCoordinator.Setup( c => c.ChangeRoomAsync( mockSession.Object, dungeonRoom.Object.RoomId, RoomTransitionReason.PlayerRequest ) )
				.Callback( () => mockSession.Object.SetCurrentRoom( dungeonRoom.Object ) )
				.ReturnsAsync( RoomTransitionResult.Success );

			var handler = MockFactoryHelper.CreateSystemPacketHandler(mockRoomManager, mockCoordinator);
			var packet = new C_ChangeRoom
			{
				RoomType = (int)RoomType.Dungeon,
				TargetId = 0
			};

			// Act
			await handler.Handlers[ typeof( C_ChangeRoom ) ]( mockSession.Object, packet );

			// Assert
			var response = Assert.Single(sentPackets) as S_ChangeRoom;
			Assert.NotNull( response );
			Assert.True( response.Success );
			Assert.Equal( 3, response.MapId );
		}

		/// <summary>
		/// 검증 5: JoinDefaultLObbyAsync가 EnterViaQueueAsync를 호출하는지
		/// RoomManager -> Room 경로가 Queue 경유인지 검증
		/// </summary>
		[Fact]
		public async Task JoinDefaultLobby_UsesEnterViaQueueAsync()
		{
			// Arrange
			var mockRoom = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, roomId:1, mapId:1);
			mockRoom.Setup( r => r.EnterViaQueueAsync( It.IsAny<IClientSession>() ) ).ReturnsAsync( RoomEnterResult.Success );

			var (mockSession, _) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: null );
			var mockRoomManager = new Mock<IRoomManager>();
			mockRoomManager.Setup( rm => rm.JoinDefaultLobbyAsync( It.IsAny<IClientSession>() ) ).Returns<IClientSession>( async session =>
			{
				return await mockRoom.Object.EnterViaQueueAsync( session );
			} );

			// Act
			RoomEnterResult result = await mockRoomManager.Object.JoinDefaultLobbyAsync(mockSession.Object);

			// Assert
			Assert.Equal( RoomEnterResult.Success, result );
			mockRoom.Verify(r => r.EnterViaQueueAsync(It.IsAny<IClientSession>()), Times.Once);
		}

		/// <summary>
		/// 검증 6: MovePlayerToRoomAsync가 LeaveViaQueueAsync + EnterViaQueueAsync 호출하는지
		/// </summary>
		[Trait("Category", "Integration")]
		[Fact(Timeout = 5000)]
		public async Task MovePlayerToRoom_UsesViaQueue()
		{
			// Arrange: 실제 LobbyRoom + DungeonRoom 인스턴스 (Mock 아님)
			var (sourceLobby, sourceJqm) = await IntegrationTestHelper.BuildAndRegisterLobbyRoomAsync(roomId: 1, roomName: "SourceLobby", maxPlayers: 4 );
			var (targetDungeon, targetJqm) = await IntegrationTestHelper.BuildAndRegisterDungeonRoomAsync( roomId: 10, roomName: "TargetDungeon", maxPlayers: 4 );
			
			try
			{
				// 실제 Coordinator + Mock IRoomManager (FindRoom Setup 만 필요)
				var (coordinator, mockRoomManager, _) = MockFactoryHelper.CreateCoordinator();
				mockRoomManager.Setup( rm => rm.FindRoom( 10 ) ).Returns( targetDungeon );

				// 실제 ClientSession + source 입장 상태
				var (session, _, _) = MockFactoryHelper.CreateRealClientSession( sessionId: 1, connected: true );
				session.SetCurrentRoom( sourceLobby );
				await sourceLobby.EnterViaQueueAsync( session );

				// Act: Coordinator.ChangeRoomAsync 호출 (Step 0 ~ 5 + finally 모두 실행)
				RoomTransitionResult result = await coordinator.ChangeRoomAsync( session, targetDungeon.RoomId, RoomTransitionReason.PlayerRequest );

				// Assert: 상태 기반 검증
				Assert.Equal( RoomTransitionResult.Success, result );
				Assert.False( sourceLobby.ContainsPlayer( session ) );								// Leave 실제 호출됨
				Assert.True(targetDungeon.ContainsPlayer( session ) );								// Enter 실제 호출됨
				Assert.Same( targetDungeon, session.CurrentRoom );									// 세션의 CurrentRoom이 최종적으로 TargetDungeon으로 갱신됨
				Assert.False( coordinator.TryGetActiveTransition( session.SessionId, out _ ) );		// finally cleanup
			}
			finally
			{
				await sourceJqm.StopAsync();
				await targetJqm.StopAsync();
			}
		}

		/// <summary>
		/// 검증 7: RemovePlayerFromAllRoomAsync가 LeaveViaQueueAsync를 호출하는지
		/// </summary>
		[Fact]
		public async Task RemovePlayerFromAllRooms_UsesLeavceViaQueueAsync()
		{
			// Arrange
			var room = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, roomId:1, mapId:1);
			room.Setup( r => r.LeaveViaQueueAsync( It.IsAny<IClientSession>() ) ).ReturnsAsync( true );

			var (mockSession, _) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: room.Object );

			var mockRoomManager = new Mock<IRoomManager>();
			mockRoomManager.Setup( rm => rm.RemovePlayerFromAllRoomsAsync( It.IsAny<IClientSession>() ) ).Returns<IClientSession>( async session =>
			{
				var currentRoom = session.CurrentRoom;
				if(currentRoom != null)
					return await currentRoom.LeaveViaQueueAsync( session );
				return false;
			} );

			// Act
			bool removed = await mockRoomManager.Object.RemovePlayerFromAllRoomsAsync(mockSession.Object);

			// Assert
			Assert.True( removed );
			room.Verify( r => r.LeaveViaQueueAsync( It.IsAny<IClientSession>() ), Times.Once );
		}
	}
}
