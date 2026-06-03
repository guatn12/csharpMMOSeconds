using Castle.Core.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Packet;
using Server.Packet.Handlers;
using Server.Room;
using Server.Tests.TestHelpers;
using ServerCore;
using System.Reflection;

namespace Server.Tests.Packet
{
	public class PacketCategoryTests
	{
		private readonly PacketManager _packetManager;

		public PacketCategoryTests()
		{
			var mockLogger = new Mock<ILogger<PacketManager>>();
			var systemPacketHandler = MockFactoryHelper.CreateSystemPacketHandler(
				new Mock<IRoomManager>(), new Mock<IRoomTransitionCoordinator>());
			var mockJobQueueManager = new Mock<IJobQueueManager>();

			_packetManager = new PacketManager( mockLogger.Object, mockJobQueueManager.Object, systemPacketHandler);
		}

		[Theory]
		// SYSTEM
		[InlineData( PacketID.C_EnterGame, PacketCategory.System )]
		[InlineData( PacketID.C_ChangeRoom, PacketCategory.System )]
		[InlineData( PacketID.C_Ping, PacketCategory.System )]
		// ROOM
		[InlineData( PacketID.C_Move, PacketCategory.Room )]
		[InlineData( PacketID.C_Chat, PacketCategory.Room )]
		[InlineData( PacketID.C_PlayerInfo, PacketCategory.Room )]
		[InlineData( PacketID.C_AutoMove, PacketCategory.Room )]
		// COMBAT
		[InlineData( PacketID.C_UseSkill, PacketCategory.Combat )]
		// INVENTORY
		[InlineData( PacketID.C_InventoryRequest, PacketCategory.Inventory )]
		[InlineData( PacketID.C_UseItem, PacketCategory.Inventory )]
		[InlineData( PacketID.C_EquipItem, PacketCategory.Inventory )]
		[InlineData( PacketID.C_UnequipItem, PacketCategory.Inventory )]
		public void GetPacketCategory_ReturnExpectedCategory( PacketID packetId, PacketCategory expectedCategory )
		{
			// Arrange: private 메서드 리플렉션 (production 코드 보호 유지)
			var method = typeof(PacketManager).GetMethod("GetPacketCategory", BindingFlags.Instance | BindingFlags.NonPublic);

			// Act
			PacketCategory result = (PacketCategory)method.Invoke(_packetManager, new object[] { packetId });
			
			// Assert
			Assert.Equal( expectedCategory, result );
		}

		[Fact]
		public void GetPacketCategory_UnregisteredPacket_ReturnsNoneCategory()
		{
			// Arrange: private 메서드 리플렉션 (production 코드 보호 유지)
			var method = typeof(PacketManager).GetMethod("GetPacketCategory", BindingFlags.Instance | BindingFlags.NonPublic);
			var unregisteredPacketId = (PacketID)9999; // 존재하지 않는 PacketID
			
			// Act
			PacketCategory result = (PacketCategory)method.Invoke(_packetManager, new object[] { unregisteredPacketId });
			
			// Assert
			Assert.Equal( PacketCategory.NoneCategory, result );
		}
	}
}
