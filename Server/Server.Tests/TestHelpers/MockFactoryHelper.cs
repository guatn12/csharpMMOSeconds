using Castle.Core.Logging;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Protocol;
using Server.Config;
using Server.Core.Session;
using Server.Data.Models;
using Server.Database.Entities;
using Server.Game;
using Server.Game.Map;
using Server.Packet.Handlers;
using Server.Room;
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

			return new SystemPacketHandler( mockLogger.Object, roomManager.Object, settings );
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

		// 4. 아이템이 있는 Session Mock
		
		/// <summary>
		/// 특정 아이템을 보유한 GameSession Mock 생성
		/// </summary>
		public static Mock<ClientSession> CreateMockSessionWithItem(int itemId, int quantity, long playerId = 1, string playerName = "TestPlayer")
		{
			var mockSession = CreateMockSession(playerId, playerName);

			bool success = mockSession.Object.Player.Inventory.AddItem( itemId, quantity );

			if(!success)
			{
				throw new InvalidOperationException(
					$"Failed to add item {itemId} x{quantity} to inventory. " +
					"Inventory may be full or invalid item data." );
			}

			return mockSession;
		}

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
		public static Mock<ClientSession> CreateMockSessionWithItems(
			params (int itemId, int quantity)[] items )
		{
			var mockSession = CreateMockSession();

			foreach(var (itemId, quantity) in items)
			{
				bool success = mockSession.Object.Player.Inventory.AddItem(
				  itemId: itemId,
				  quantity: quantity,
				  options: null
			  );

				if(!success)
				{
					throw new InvalidOperationException(
						$"Failed to add item {itemId} x{quantity}. " +
						"Check inventory space or item validity." );
				}
			}

			return mockSession;
		}
	}
}
