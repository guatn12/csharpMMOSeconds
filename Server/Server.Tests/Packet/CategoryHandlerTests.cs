using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Moq;
using Protocol;
using Server.Config;
using Server.Core.Session;
using Server.Game;
using Server.Game.Monsters;
using Server.Packet;
using Server.Packet.Handlers;
using Server.Room;
using Server.Services.Combat;
using Server.Services.Reward;
using Server.Tests.TestHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Packet
{
	/// <summary>
	/// Category 핸들러 통합 테스트
	/// 각 핸들러의 비즈니스 로직 검증
	/// </summary>
	public class CategoryHandlerTests
	{
		[Fact]
		public async Task SystemPacketHandler_HandleC_EnterGame_ShouldJoinDefaultLobby()
		{
			// Arrange
			var lobbyRoom = MockFactoryHelper.CreateMockRoom(RoomType.Lobby, roomId: 1, mapId:1);
			var (mockSession, _) = MockFactoryHelper.CreateSessionMock( playerId: 1, initialRoom: null );
			var sendToPlayer = new List<IMessage>();
			lobbyRoom.Setup( r => r.SendToPlayer( It.IsAny<IClientSession>(), It.IsAny<IMessage>() ) )
				.Callback<IClientSession, IMessage>( ( s, p ) =>
				{
					sendToPlayer.Add( p );
				} );

			var mockRoomManager = new Mock<IRoomManager>();
			mockRoomManager.Setup( rm => rm.JoinDefaultLobbyAsync( It.IsAny<IClientSession>() ) )
				.Returns<IClientSession>( se =>
				{
					se.SetCurrentRoom( lobbyRoom.Object );
					return Task.FromResult( RoomEnterResult.Success );
				} );

			var mockCoordinator = new Mock<IRoomTransitionCoordinator>();
			var handler = MockFactoryHelper.CreateSystemPacketHandler(mockRoomManager, mockCoordinator);

			// Act
			await handler.Handlers[ typeof( C_EnterGame ) ]( mockSession.Object, new C_EnterGame() );

			// Assert
			mockRoomManager.Verify( rm => rm.JoinDefaultLobbyAsync( It.IsAny<IClientSession>() ), Times.Once );

			var response = Assert.Single(sendToPlayer) as S_EnterGame;
			Assert.NotNull( response );
			Assert.Equal( 1, response.MapId );
		}
	}
}
