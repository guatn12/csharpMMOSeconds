using Microsoft.Extensions.Logging;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Game;
using Server.Packet;
using Server.Packet.Handlers;
using Server.Room;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests
{
	/// <summary>
	/// PacketManager의 라우팅 로직 테스트
	/// - GetPacketCategory() 매핑 검증
	/// - HandlePacket() 핸들러 라우팅 검증
	/// </summary>
	public class PacketManagerRoutingTests : IDisposable
	{
		private readonly SessionManager _sessionManager;
		private readonly PacketManager _packetManager;
		private readonly Mock<ILogger<PacketManager>> _mockLogger;

		public PacketManagerRoutingTests()
		{
			_mockLogger = new Mock<ILogger<PacketManager>>();
			var mockSystemPacketHandlerLogger = new Mock<ILogger<SystemPacketHandler>>();
			var mockRoomManager = new Mock<IRoomManager>();
			_packetManager = new PacketManager( _mockLogger.Object,
				new SystemPacketHandler( mockSystemPacketHandlerLogger.Object, mockRoomManager.Object ) );

			// SessionManager 생성 (모든 의존성을 null로 전달)
			var mockLogger = new Mock<ILogger<SessionManager>>();
			var mockServiceProvider = new Mock<IServiceProvider>();

			_sessionManager = new SessionManager(
				mockLogger.Object,
				mockServiceProvider.Object,
				redisService: null,
				playerPositionService: null
			);

			var mockIRoom = new Mock<IRoom>();
			//var mockSystemPacketHandler = new Mock<SystemPacketHandler>();
			var mockCombatPacketHandler = new Mock<CombatPacketHandler>();
			var mockInventoryPacketHandler = new Mock<InventoryPacketHandler>();
			var mockRoomPacketHandler = new Mock<RoomPacketHandler>();
		}

		public void Dispose()
		{

		}

		//[Fact]
		//public async Task HandlePacket_SystemCategory_ShouldCallSystemPacketHandler()
		//{
		//	// Arrange
		//	var session = CreateTestGameSession(sessionId: 1, playerId: 1001);
		//	var mockRoom = CreateMockRoom();
		//	var mockLogger = new Mock<ILogger<SystemPacketHandler>>();
		//	var SystemHandler = new SystemPacketHandler(mockLogger.Object);

		//	mockRoom.Setup( r => r.SystemPacketHandler ).Returns( SystemHandler );
		//	SetCurrentRoom( session, mockRoom.Object );

		//	// C_EnterGame 패킷 버퍼 생성
		//	var packet = new C_EnterGame();
		//	var buffer = _packetManager.MakeSendPacket( packet );
		//	//var buffer = CreatePacketBuffer(PacketID.C_EnterGame, packet);

		//	// Act
		//	await _packetManager.HandlePacket( session, buffer );

		//	// Assert
		//	mockLogger.Verify(
		//		x => x.Log( LogLevel.Debug,
		//		It.IsAny<EventId>(),
		//		It.Is<It.IsAnyType>( ( v, t ) => v.ToString().Contains( "entered game" ) ),
		//		It.IsAny<Exception>(),
		//		It.IsAny<Func<It.IsAnyType, Exception, string>>() ),
		//		Times.Once );
		//}

		/// <summary>
		/// 테스트용 GameSession 객체 생성 (리플렉션 사용)
		/// </summary>
		private GameSession CreateTestGameSession( long sessionId, long playerId )
		{
			// GameSession 생성자는 ILogger, PacketManager, ISessionManager, sessionId 필요
			var mockLogger = new Mock<ILogger<GameSession>>();

			// GameSession 생성
			var session = (GameSession)Activator.CreateInstance(
				typeof(GameSession),
				BindingFlags.Instance | BindingFlags.Public,
				null,
				new object[] { mockLogger.Object, null, _sessionManager, sessionId },
				null
			);

			// Player 초기화 (InitializePlayer private 메서드 호출)
			var initializePlayerMethod = typeof(GameSession).GetMethod(
				"InitializePlayer",
				BindingFlags.Instance | BindingFlags.NonPublic
			);
			initializePlayerMethod.Invoke( session, null );

			// Player의 PlayerId 변경 (리플렉션)
			var playerProperty = typeof(GameSession).GetProperty("Player");
			var player = (Player)playerProperty.GetValue(session);

			// Player.Info.PlayerId 변경
			var infoProperty = typeof(Player).GetProperty("Info");
			var info = infoProperty.GetValue(player);
			var playerIdField = info.GetType().GetProperty("PlayerId");
			playerIdField.SetValue( info, playerId );

			return session;
		}

		/// <summary>
		/// Mock IRoom 생성
		/// </summary>
		private Mock<IRoom> CreateMockRoom( int roomId = 1 )
		{
			var mockRoom = new Mock<IRoom>();
			mockRoom.Setup( r => r.RoomId ).Returns( roomId );
			mockRoom.Setup( r => r.RoomName ).Returns( "TestRoom" );
			mockRoom.Setup( r => r.ContainsPlayer( It.IsAny<GameSession>() ) ).Returns( true );
			mockRoom.Setup( r => r.ContainsPlayerToPlayerId( It.IsAny<long>() ) ).Returns( true );

			return mockRoom;
		}

		/// <summary>
		/// GameSession의 CurrentRoom을 Reflection으로 설정
		/// </summary>
		private void SetCurrentRoom( GameSession session, IRoom room )
		{
			var field = typeof(GameSession).GetField("_currentRoom", BindingFlags.Instance | BindingFlags.NonPublic);
			field?.SetValue( session, room );
		}
	}
}
