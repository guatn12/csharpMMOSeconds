using Xunit;
using Server.Tests.TestHelpers;
using Protocol;
using Microsoft.Extensions.Logging;

namespace Server.Tests.TestHelpers
{
	public class MockFactoryTests
	{
		[Fact]
		public void CreateMockRoom_ShoulReturnValidMock()
		{
			// arrange & Act
			var mockRoom = MockFactory.CreateMockRoom(roomId: 999);

			// Assert
			Assert.NotNull( mockRoom );
			Assert.NotNull( mockRoom.Object );
			Assert.Equal(999, mockRoom.Object.RoomId );
		}

		[Fact]
		public void CreateMockRoom_ContainsPlayer_ShouldReturnTest()
		{
			// Arrange
			var mockRoom = MockFactory.CreateMockRoom();

			// Act
			bool result = mockRoom.Object.ContainsPlayer(null);

			// Assert
			Assert.True( result );
		}

		[Fact]

		public async Task CreateMockRoom_BroadcastAsync_ShouldComplete()
		{
			// Arrage
			var mockRoom = MockFactory.CreateMockRoom();
			var packet = new S_Move();

			// Act
			await mockRoom.Object.BroadcastAsync( packet, null );

			// Assert
			// 예외 없이 완료되면 성공
			Assert.True( true );
		}

		[Fact]
		public void CreateMockSession_ShouldReturnValidSession()
		{
			// Arrange & Act
			var mockSession = MockFactory.CreateMockSession(
			  playerId: 123,
			  playerName: "TestHero"
		  );

			// Assert
			Assert.NotNull( mockSession );
			Assert.NotNull( mockSession.Object.Player );

			// Player 생성자에서 자동 설정되는 값들 확인
			Assert.Equal( 123, mockSession.Object.Player.PlayerId );
			Assert.Equal( "TestHero", mockSession.Object.Player.Info.Name );
			Assert.Equal( 1, mockSession.Object.Player.Info.Level );  // 기본 레벨 1
			Assert.Equal( 100, mockSession.Object.Player.CurrentHP );  // 기본 HP 100
			Assert.Equal( 50, mockSession.Object.Player.CurrentMP );   // 기본 MP 50
		}

		[Fact]
		public void CreateMockSessionWithItem_ShouldHaveItem()
		{
			// Arrange & Act
			var mockSession = MockFactory.CreateMockSessionWithItem(
			  itemId: 1001,
			  quantity: 5
		  );

			// Assert
			var inventory = mockSession.Object.Player.Inventory;

			var items = inventory.GetItemsByType(itemId: 1001);
			Assert.NotEmpty( items );
			Assert.Equal( 5, items[ 0 ].Quantity );

			// 또는 총 수량으로 확인
			int totalQuantity = inventory.GetItemQuantity(itemId: 1001);
			Assert.Equal( 5, totalQuantity );
		}

		[Fact]
		public void CreateMockSessionWithItems_ShouldHaveMultipleItems()
		{
			// Arrange & Act
			var mockSession = MockFactory.CreateMockSessionWithItems(
			  (itemId: 1001, quantity: 3),  // HP 포션 3개
              (itemId: 1002, quantity: 5),  // MP 포션 5개
              (itemId: 2001, quantity: 1)   // 무기 1개
          );

			// Assert
			var inventory = mockSession.Object.Player.Inventory;

			Assert.Equal( 3, inventory.GetItemQuantity( 1001 ) );
			Assert.Equal( 5, inventory.GetItemQuantity( 1002 ) );
			Assert.Equal( 1, inventory.GetItemQuantity( 2001 ) );
			Assert.Equal( 3, inventory.UsedSlots );  // 3개 슬롯 사용
		}

		[Fact]
		public void CreateMockLogger_ShouldReturnValidLogger()
		{
			// Arrange & Act
			var mockLogger = MockFactory.CreateMockLogger();

			// Assert
			Assert.NotNull( mockLogger );
			Assert.NotNull( mockLogger.Object );

			// 로그 메서드 호출해도 예외 없음
			mockLogger.Object.LogInformation( "테스트 로그" );
		}
	}
}
