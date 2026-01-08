using Castle.Core.Logging;
using Microsoft.Extensions.Logging;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Packet;
using Server.Packet.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests
{
	public class PacketCategoryTests
	{
		private readonly PacketManager _packetManager;

		public PacketCategoryTests()
		{
			var mockLogger = new Mock<ILogger<PacketManager>>();
			var mockSystemPacketHandler = new Mock<ILogger<SystemPacketHandler>>();
			var mockServiceProvider = new Mock<IServiceProvider>();

			_packetManager = new PacketManager( mockLogger.Object,
				new SystemPacketHandler( mockSystemPacketHandler.Object ) );
		}

		[Fact]
		public void PacketCategory_System()
		{
			// Arrange
			var method = typeof(PacketManager).GetMethod("GetPacketCategory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var packetId = PacketID.C_EnterGame;

			// Act
			PacketCategory result = (PacketCategory)method.Invoke(_packetManager, [packetId]);

			// Assert
			Assert.Equal( PacketCategory.System , result);
		}

		[Fact]
		public void PacketCategory_Room()
		{
			// Arrange
			var method = typeof(PacketManager).GetMethod("GetPacketCategory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var packetIdMove = PacketID.C_Move;
			var packetIdChat = PacketID.C_Chat;
			var packetPlayerInfo = PacketID.C_PlayerInfo;
			var packetUseSkill = PacketID.C_UseSkill;

			// Act
			PacketCategory result1 = (PacketCategory)method.Invoke(_packetManager, [packetIdMove]);
			PacketCategory result2 = (PacketCategory)method.Invoke(_packetManager, [packetIdChat]);
			PacketCategory result3 = (PacketCategory)method.Invoke(_packetManager, [packetPlayerInfo]);
			PacketCategory result4 = (PacketCategory)method.Invoke(_packetManager, [packetUseSkill]);

			// Assert
			Assert.Equal( PacketCategory.Room, result1 );
			Assert.Equal( PacketCategory.Room, result2 );
			Assert.Equal( PacketCategory.Room, result3 );
			Assert.Equal( PacketCategory.Room, result4 );
		}

		[Fact]
		public void PacketCategory_Inventory()
		{
			// Arrange
			var method = typeof(PacketManager).GetMethod("GetPacketCategory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var packetInventoryRequest = PacketID.C_InventoryRequest;
			var packetUseItem = PacketID.C_UseItem;
			var packetEquipItem = PacketID.C_EquipItem;
			var packetUnequipItem = PacketID.C_UnequipItem;

			// Act
			PacketCategory result1 = (PacketCategory)method.Invoke(_packetManager, [packetInventoryRequest]);
			PacketCategory result2 = (PacketCategory)method.Invoke(_packetManager, [packetUseItem]);
			PacketCategory result3 = (PacketCategory)method.Invoke(_packetManager, [packetEquipItem]);
			PacketCategory result4 = (PacketCategory)method.Invoke(_packetManager, [packetUnequipItem]);

			// Assert
			Assert.Equal( PacketCategory.Inventory, result1 );
			Assert.Equal( PacketCategory.Inventory, result2 );
			Assert.Equal( PacketCategory.Inventory, result3 );
			Assert.Equal( PacketCategory.Inventory, result4 );
		}

		[Fact]
		public void PacketCategory_Combat()
		{
			// Arrange
			var method = typeof(PacketManager).GetMethod("GetPacketCategory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			var packetAttackMonster = PacketID.C_AttackMonster;

			// Act
			PacketCategory result1 = (PacketCategory)method.Invoke(_packetManager, [packetAttackMonster]);

			// Assert
			Assert.Equal( PacketCategory.Combat, result1 );

			
		}
	}
}
