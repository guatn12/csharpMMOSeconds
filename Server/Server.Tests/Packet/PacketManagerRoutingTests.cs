using Microsoft.Extensions.Logging;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Room;
using Server.Tests.TestHelpers;
using ServerCore;

namespace Server.Tests.Packet
{
	public class PacketManagerRoutingTests
	{
		[Fact]
		public async Task HandlePacket_SystemCategory_ShouldCallSystemPacketHandler()
		{
			// Arrange
			var (session, _, _) = MockFactoryHelper.CreateRealClientSession(sessionId: 1, connected: true);

			int joinLobbyCallCount = 0;
			var mockRoomManager = new Mock<IRoomManager>();
			mockRoomManager.Setup( rm => rm.JoinDefaultLobbyAsync( It.IsAny<IClientSession>() ) )
				.Returns<IClientSession>( s =>
				{
					Interlocked.Increment( ref joinLobbyCallCount );
					var mockLobby = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, roomId:1, mapId:1);
					s.SetCurrentRoom( mockLobby.Object );
					return Task.FromResult( RoomEnterResult.Success );
				} );

			var mockCoordinator = new Mock<IRoomTransitionCoordinator>();
			var systemHandler = MockFactoryHelper.CreateSystemPacketHandler( mockRoomManager, mockCoordinator );

			var loggerFactory = LoggerFactory.Create(b => { });
			var jobQueueManager = new JobQueueManager(loggerFactory.CreateLogger<JobQueueManager>());
			var (packetManager, _) = MockFactoryHelper.CreateRealPacketManager( jobQueueManager, systemHandler );

			// C_EnterGame (SYSTEM 카테고리) 패킷 버퍼 생성
			var packet = new C_EnterGame();
			var buffer = MockFactoryHelper.SerializeClientPacket(PacketID.C_EnterGame, packet);

			// Act: PacketManager.HandlePacket이 SYSTEM 카테고리 -> SystemPacketHandler 라우팅
			await packetManager.HandlePacket( session, buffer );

			// Assert: SystemPacketHandler를 통해 JoinDefaultLobbyAsync가 호출됨 -> 라우팅 성공
			Assert.Equal( 1, joinLobbyCallCount );
			Assert.Equal( SessionState.InRoom, session.State );
		}
	}
}
