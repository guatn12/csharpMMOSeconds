using Castle.Core.Logging;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Protocol;
using Server.Config;
using Server.Core.Session;
using Server.Data.Models;
using Server.Database.Entities;
using Server.Game;
using Server.Game.Map;
using Server.Infra;
using Server.Packet;
using Server.Packet.Handlers;
using Server.Room;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.TestHelpers
{
	/// <summary>
	/// 테스트에서 사용할 Mock 객체를 생성하는 팩토리 클래스
	/// 재사용 가능한 Mock 생성 로직을 중앙화.
	/// </summary>
	public class MockFactoryHelper
	{
		public static TickService CreateTickService(int baseTickMs = 100)
		{
			var mockLogger = new Mock<ILogger<TickService>>();
			var settings = Options.Create(new ServerSettings
			{
				Tick = new TickConfig{BaseTickMs = baseTickMs },
			});

			return new TickService( mockLogger.Object, settings );
		}

		// 1. BaseRoom Mock
		/// <summary>
		/// 기본 설정이 된 BaseRoom Mock 생성
		/// </summary>
		public static Mock<IRoom> CreateMockRoom(RoomType roomType, int roomId, int mapId)
		{
			Mock<IRoom> mockRoom = new Mock<IRoom>();

			MapData mapData = MapData.CreateEmpty(10, 10);
			mapData.Id = mapId;
			GameMap gameMap = new GameMap(mapData);

			mockRoom.Setup(r => r.RoomType).Returns(roomType);
			mockRoom.Setup(r => r.RoomId).Returns(roomId);
			mockRoom.Setup( r => r.RoomMap ).Returns( gameMap );

			return mockRoom;
		}

		public static SystemPacketHandler CreateSystemPacketHandler(Mock<IRoomManager> roomManager, int maxPlayers = 4)
		{
			var (coordinator, mockRoomManager, mockSessionManager) = CreateCoordinator();
			Mock<ILogger<SystemPacketHandler>> mockLogger = new Mock<ILogger<SystemPacketHandler>>();
			var settings = Options.Create( new ServerSettings
			{
				Room = new RoomConfig
				{
					Dungeon = new DungeonConfig
					{
						DefaultName = "default",
						MaxPlayers = maxPlayers
					},
					Lobby = new LobbyConfig { DefaultName = "default", MaxPlayers = maxPlayers },
					Battle = new BattleConfig { DefaultName = "default", MaxPlayers= maxPlayers },
					Guild = new GuildConfig { DefaultName = "default", MaxPlayers = maxPlayers },
					Private = new PrivateConfig { DefaultName = "default", MaxPlayers = maxPlayers },
					EmptyRoomCleanupIntervalMinutes = 0,
					MaxRoomNameLength = 255,
					MaxRooms = 100,
					TickIntervalMs = 100
				}
			} );

			return new SystemPacketHandler( mockLogger.Object, roomManager.Object, settings, coordinator );
		}

		/// <summary>
		/// IClientSession mock 생성 + Send 캡처 헬퍼
		/// </summary>
		public static (Mock<IClientSession> mockSession, List<IMessage> sentPackets) CreateSessionMock( long playerId, IRoom initialRoom )
		{
			var sentPackets = new List<IMessage>();
			IRoom currentRoom = initialRoom;
			var player = new Player(playerId, $"TestPlayer{playerId}");

			var mockSession = new Mock<IClientSession>();
			mockSession.Setup( s => s.Player ).Returns( player );
			mockSession.Setup( s => s.PlayerId ).Returns( playerId );
			mockSession.Setup( s => s.SessionId ).Returns( playerId );
			mockSession.Setup( s => s.CurrentRoom ).Returns( () => currentRoom );
			mockSession.Setup( s => s.SetCurrentRoom( It.IsAny<IRoom>() ) ).Callback<IRoom>( r => currentRoom = r );
			mockSession.Setup( s => s.Send( It.IsAny<IMessage>() ) ).Callback<IMessage>( p => sentPackets.Add( p ) );

			return (mockSession, sentPackets);
		}

		/// <summary>
		/// 진짜 ClientSession 인스턴스를 외부 종속성 Mock과 함께 생성.
		/// connected=true 면 OnConnected 정상 진입 경로를 호출 -> Player + 이벤트 구독 완료.
		/// pakcetManager=null 이면 Send 경로는 NRE - Send를 건드리지 않는 테스트에서만 null 허용.
		/// </summary>
		public static (ClientSession session, Mock<ILogger<ClientSession>> mockLogger, Mock<ISessionManager> mockSessionManager)
			CreateRealClientSession(long sessionId = 1, PacketManager packetManager = null, bool connected = false)
		{
			var mockLogger = new Mock<ILogger<ClientSession>>();
			var mockSessionManager = new Mock<ISessionManager>();

			packetManager ??= CreateMinimalPacketManager();

			var session = new ClientSession(mockLogger.Object, packetManager, mockSessionManager.Object, sessionId);
			
			if (connected)
			{
				// 정상 진입 경로 - InitializePlayer + RegisterSession 자연 호출
				// _socket을 사용하지 않으므로 NetworkSession.Start 없이 안전
				session.OnConnected(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 12345));
			}
			return (session, mockLogger, mockSessionManager);
		}

		/// <summary>
		/// 비동기 상태 전이를 결정적으로 대기. flaky 방지용.
		/// OnDisConnected는 fire-and-forget 정리 Task를 띄우므로 폴링 필요.
		/// </summary>
		public static async Task WaitForStateAsync(IClientSession session, SessionState expected, int timeoutMs = 1000)
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();
			while(sw.ElapsedMilliseconds < timeoutMs)
			{
				if(session.State == expected) return;
				await Task.Delay( 10 );
			}

			throw new TimeoutException( $"State {expected} not reached in {timeoutMs}ms (current: {session.State}, SessionId: {session.SessionId}" );
		}

		/// <summary>
		/// 진짜 PacketManager 인스턴스 (1차 필터 동작 검증용)
		/// </summary>
		public static (PacketManager packetManager, Mock<ILogger<PacketManager>> mockLogger) CreateRealPacketManager(IJobQueueManager jobQueueManager, 
			SystemPacketHandler systemHandler)
		{
			var mockLogger = new Mock<ILogger<PacketManager>>();
			var packetManager = new PacketManager(mockLogger.Object, jobQueueManager, systemHandler);

			return (packetManager, mockLogger);
		}

		/// <summary>
		/// C_* 패킷을 [Size(2)|Id(2)|Body] 구조로 직렬화.
		/// MakeSendPacket이 S_*만 처리하므로 별도 헬퍼
		/// </summary>
		public static ArraySegment<byte> SerializeClientPacket( PacketID id, IMessage packet )
		{
			ushort size = (ushort)packet.CalculateSize();
			ushort total = (ushort)(size + 4);
			byte[] buffer = new byte[total];

			Array.Copy( BitConverter.GetBytes( total ), 0, buffer, 0, 2 );
			Array.Copy(BitConverter.GetBytes( (ushort)id ), 0, buffer, 2, 2 );
			packet.WriteTo( new MemoryStream( buffer, 4, size ) );
			return new ArraySegment<byte>( buffer );
		}

		// 3. ILogger Mock
		///<summary>
		/// ILogger Mock 생성 (로그 출력 없음)
		/// </summary>
		public static Mock<Microsoft.Extensions.Logging.ILogger> CreateMockLogger()
		{
			var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger>();

			// 모든 로그 메서드 비활성화
			mockLogger.Setup( l => l.Log(
				It.IsAny<LogLevel>(),
				It.IsAny<EventId>(),
				It.IsAny<It.IsAnyType>(),
				It.IsAny<Exception>(),
				It.IsAny<Func<It.IsAnyType, Exception, string>>() ) );

			return mockLogger;
		}

		private static PacketManager CreateMinimalPacketManager()
		{
			var maxPlayers = 4;
			var loggerFactory = LoggerFactory.Create(b => { });
			var jq = new JobQueueManager(NullLogger<JobQueueManager>.Instance);
			//var mockRoomManager = new Mock<IRoomManager>();
			var (coordinator, mockRoomManager, mockSessionManager) = CreateCoordinator();

			var settings = Options.Create( new ServerSettings
			{
				Room = new RoomConfig
				{
					Dungeon = new DungeonConfig
					{
						DefaultName = "default",
						MaxPlayers = maxPlayers
					},
					Lobby = new LobbyConfig { DefaultName = "default", MaxPlayers = maxPlayers },
					Battle = new BattleConfig { DefaultName = "default", MaxPlayers= maxPlayers },
					Guild = new GuildConfig { DefaultName = "default", MaxPlayers = maxPlayers },
					Private = new PrivateConfig { DefaultName = "default", MaxPlayers = maxPlayers },
					TickIntervalMs = 100
				},
				Tick = new TickConfig { BaseTickMs = 100 }
			} );
			var systemHandler = new SystemPacketHandler( NullLogger<SystemPacketHandler>.Instance, mockRoomManager.Object, settings, coordinator );
			return new PacketManager( NullLogger<PacketManager>.Instance, jq, systemHandler );
		}

		public static (RoomTransitionCoordinator coordinator,
			Mock<IRoomManager> mockRoomManager,
			Mock<ISessionManager> mockSessionManager)
			CreateCoordinator()
		{
			var mockRoomManager = new Mock<IRoomManager>();
			var mockSessionManager = new Mock<ISessionManager>();
			var coordinator = new RoomTransitionCoordinator(NullLogger<RoomTransitionCoordinator>.Instance,
				mockRoomManager.Object, mockSessionManager.Object);

			return (coordinator, mockRoomManager, mockSessionManager);
		}

		// 4. 아이템이 있는 Session Mock

		/// <summary>
		/// 특정 아이템을 보유한 GameSession Mock 생성
		/// </summary>
		//public static Mock<ClientSession> CreateMockSessionWithItem(int itemId, int quantity, long playerId = 1, string playerName = "TestPlayer")
		//{
		//	var mockSession = CreateMockSession(playerId, playerName);

		//	bool success = mockSession.Object.Player.Inventory.AddItem( itemId, quantity );

		//	if(!success)
		//	{
		//		throw new InvalidOperationException(
		//			$"Failed to add item {itemId} x{quantity} to inventory. " +
		//			"Inventory may be full or invalid item data." );
		//	}

		//	return mockSession;
		//}

		// ===== 5. 특정 레벨/HP/MP를 가진 Session Mock =====

		/// <summary>
		/// 특정 스탯을 가진 GameSession Mock 생성
		///
		/// 학습 포인트:
		/// - Player 생성 후 Info 속성을 직접 수정
		/// - Player.Info는 public get이므로 읽을 수만 있고 쓸 수는 없음
		/// - 대신 Player의 메서드(TakeDamage, Heal 등)를 사용해야 함
		/// </summary>
		//public static Mock<ClientSession> CreateMockSessionWithStats(
		//	long playerId = 1,
		//	string playerName = "TestPlayer",
		//	int? currentHP = null,
		//	int? currentMP = null )
		//{
		//	var mockSession = CreateMockSession(playerId, playerName);
		//	var player = mockSession.Object.Player;

		//	// HP 조정 (TakeDamage 또는 Heal 사용)
		//	if(currentHP.HasValue && currentHP.Value < player.CurrentHP)
		//	{
		//		int damage = player.CurrentHP - currentHP.Value;
		//		player.TakeDamage( damage );
		//	}

		//	// MP 조정 (ConsumeMana 사용)
		//	if(currentMP.HasValue && currentMP.Value < player.CurrentMP)
		//	{
		//		int manaCost = player.CurrentMP - currentMP.Value;
		//		// ConsumeMana 메서드가 있다면 사용
		//		// 없다면 Info를 직접 수정할 방법이 없음
		//	}

		//	return mockSession;
		//}

		// ===== 6. 여러 아이템을 가진 Session Mock =====

		/// <summary>
		/// 여러 아이템을 보유한 GameSession Mock 생성
		/// </summary>
		//public static Mock<ClientSession> CreateMockSessionWithItems(
		//	params (int itemId, int quantity)[] items )
		//{
		//	var mockSession = CreateMockSession();

		//	foreach(var (itemId, quantity) in items)
		//	{
		//		bool success = mockSession.Object.Player.Inventory.AddItem(
		//		  itemId: itemId,
		//		  quantity: quantity,
		//		  options: null
		//	  );

		//		if(!success)
		//		{
		//			throw new InvalidOperationException(
		//				$"Failed to add item {itemId} x{quantity}. " +
		//				"Check inventory space or item validity." );
		//		}
		//	}

		//	return mockSession;
		//}
	}
}
