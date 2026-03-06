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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests
{
	/// <summary>
	/// Category 핸들러 통합 테스트
	/// 각 핸들러의 비즈니스 로직 검증
	/// </summary>
	public class CategoryHandlerTests : IDisposable
	{
		//private readonly SessionManager _sessionManager;
		//private readonly PacketManager _packetManager;

		//public CategoryHandlerTests()
		//{
		//	// SessionManager 생성 (CreateTestGameSession에서 필요)
		//	var mockLogger = new Mock<ILogger<SessionManager>>();
		//	var mockServiceProvider = new Mock<IServiceProvider>();
		//	_sessionManager = new SessionManager(
		//		mockLogger.Object,
		//		mockServiceProvider.Object,
		//		redisService: null,
		//		playerPositionService: null
		//	);

		//	var mockPacketManagerLogger = new Mock<ILogger<PacketManager>>();
		//	var mockSystemPacketHandlerLogger = new Mock<ILogger<SystemPacketHandler>>();
		//	var mockRoomManager = new Mock<IRoomManager>();
		//	_packetManager = new PacketManager( mockPacketManagerLogger.Object,
		//		new SystemPacketHandler( mockSystemPacketHandlerLogger.Object, mockRoomManager.Object ) );
		//}

		///// <summary>
		///// 테스트용 Room 클래스
		///// </summary>
		//private class TestRoom : BaseRoom
		//{
		//	public List<IMessage> SentMessages { get; } = new List<IMessage>();
		//	public List<IMessage> BroadcastMessages { get; } = new List<IMessage>();

		//	public TestRoom()
		//		: base(
		//			logger: new Mock<ILogger<TestRoom>>().Object,
		//			loggerFactory: new Mock<ILoggerFactory>().Object,
		//			roomName: "TestRoom",
		//			maxPlayers: 100,
		//			dataManager: null,
		//			combatService: new Mock<ICombatService>().Object,
		//			rewardService: new Mock<IRewardService>().Object,
		//			playerPositionService: null
		//		)
		//	{
		//	}

		//	public override RoomType RoomType => RoomType.Lobby;

		//	// 전송된 패킷 캡처
		//	public override async Task SendToPlayerAsync( IClientSession session, IMessage packet )
		//	{
		//		SentMessages.Add( packet );
		//		await Task.CompletedTask;
		//	}

		//	// 브로드캐스트 패킷 캡처
		//	public override async Task BroadcastAsync( IMessage packet, IClientSession excludeSession = null )
		//	{
		//		BroadcastMessages.Add( packet );
		//		await Task.CompletedTask;
		//	}
		//}

		public void Dispose()
		{

		}

		//[Fact]
		//public async Task SystemPacketHandler_HandleC_EnterGame_ShouldJoinDefaultLobby()
		//{
		//	// Arrange
		//	var mockLogger = new Mock<ILogger<SystemPacketHandler>>();
		//	var mockRoomManager = new Mock<IRoomManager>();
		//	mockRoomManager.Setup(x => x.JoinDefaultLobbyAsync(It.IsAny<ClientSession>()))
		//		.ReturnsAsync(RoomEnterResult.Success);
		//	var handler = new SystemPacketHandler(mockLogger.Object, mockRoomManager.Object);

		//	var session = CreateTestGameSession(sessionId: 1, playerId: 1001);
		//	var packet = new C_EnterGame();
		//	var buffer = CreatePacketBuffer(PacketID.C_EnterGame, packet);

		//	// Act
		//	await handler.HandleAsync( session, (ushort)PacketID.C_EnterGame, buffer );

		//	// Assert - 로비 입장 호출 및 로그 검증
		//	mockRoomManager.Verify(x => x.JoinDefaultLobbyAsync(session), Times.Once);
		//	mockLogger.Verify(
		//		x => x.Log(
		//			LogLevel.Information,
		//			It.IsAny<EventId>(),
		//			It.Is<It.IsAnyType>( ( v, t ) => v.ToString().Contains( "automatically joined the default lobby" ) ),
		//			It.IsAny<Exception>(),
		//			It.IsAny<Func<It.IsAnyType, Exception, string>>() ),
		//		Times.Once );
		//}

		//[Fact]
		//public async Task InventoryHandler_InventoryRequest()
		//{
		//	// Arrange
		//	var mockLogger = new Mock<ILogger<InventoryPacketHandler>>();
		//	var room = (TestRoom)CreateTestRoom();
		//	var handler = new InventoryPacketHandler(mockLogger.Object, room);

		//	var session = CreateTestGameSession(sessionId: 1, playerId: 1001);
		//	AddPlayerToRoom( room, session );  // Room에 플레이어 추가

		//	// 플레이어 인벤토리에 테스트 데이터 추가.
		//	session.Player.Inventory.AddGold( 5000 );

		//	var packet = new C_InventoryRequest();
		//	var buffer = CreatePacketBuffer(PacketID.C_InventoryRequest, packet);

		//	// Act
		//	await handler.HandleAsync(session, (ushort)PacketID.C_InventoryRequest, buffer );

		//	// Assert - 비즈니스 로직: 인벤토리 데이터 전송
		//	Assert.Single( room.SentMessages );  // 1개 패킷 전송
		//	var sentData = room.SentMessages[0] as S_InventoryData;
		//	Assert.NotNull( sentData );
		//	Assert.Equal( session.Player.Inventory.MaxSlots, sentData.MaxSlots );
		//	Assert.Equal( 5000, sentData.Gold );
		//}

		/// <summary>
		/// 테스트용 GameSession 생성 (SessionManagerTests 패턴 재사용)
		/// </summary>
		//private ClientSession CreateTestGameSession( long sessionId, long playerId )
		//{
		//	var mockLogger = new Mock<ILogger<ClientSession>>();

		//	// GameSession 생성
		//	var session = (ClientSession)Activator.CreateInstance(
		//		  typeof(ClientSession),
		//		  BindingFlags.Instance | BindingFlags.Public,
		//		  null,
		//		  new object[] { mockLogger.Object, null, _sessionManager, sessionId },
		//		  null
		//	  );

		//	// Player 초기화
		//	var initializePlayerMethod = typeof(ClientSession).GetMethod(
		//		  "InitializePlayer",
		//		  BindingFlags.Instance | BindingFlags.NonPublic
		//	  );
		//	initializePlayerMethod.Invoke( session, null );

		//	// Player의 PlayerId 변경 (리플렉션)
		//	var playerProperty = typeof(ClientSession).GetProperty("Player");
		//	var player = (Player)playerProperty.GetValue(session);

		//	var infoProperty = typeof(Player).GetProperty("Info");
		//	var info = infoProperty.GetValue(player);
		//	var playerIdField = info.GetType().GetProperty("PlayerId");
		//	playerIdField.SetValue( info, playerId );

		//	return session;
		//}

		//private ArraySegment<byte> CreatePacketBuffer(PacketID packetId, IMessage packet)
		//{
		//	var packetData = packet.ToByteArray();

		//	return new ArraySegment<byte>( packetData );
		//}

		///// <summary>
		///// TestRoom 생성 (BaseRoom 대신 실제 인스턴스 사용)
		///// </summary>
		//private TestRoom CreateTestRoom()
		//{
		//	return new TestRoom();
		//}

		///// <summary>
		///// Room에 플레이어 추가 (Reflection 사용)
		///// </summary>
		//private void AddPlayerToRoom( BaseRoom room, ClientSession session )
		//{
		//	var playersField = typeof(BaseRoom).GetField( "_players", BindingFlags.NonPublic | BindingFlags.Instance );
		//	var players = (System.Collections.Concurrent.ConcurrentDictionary<long, ClientSession>)playersField.GetValue( room );
		//	players.TryAdd( session.SessionId, session );
		//}
	}
}
