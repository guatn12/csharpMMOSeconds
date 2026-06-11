using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Packet;
using Server.Room;
using Server.Tests.TestHelpers;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Packet
{
	public class RouteIncomingTests
	{
		// jq는 RouteIncoming의 JobPool 제공용 - Start 하지 않는다.(순수 라우팅만 검증)
		private static (PacketManager pm, ClientSession session) Build(bool connected = true)
		{
			var jq = new JobQueueManager(NullLogger<JobQueueManager>.Instance);
			var roomMgr = new Mock<IRoomManager>();
			var systemHandler = MockFactoryHelper.CreateSystemPacketHandler(roomMgr, new Mock<IRoomTransitionCoordinator>());
			var (pm, _) = MockFactoryHelper.CreateRealPacketManager( jq, systemHandler );
			var session = MockFactoryHelper.CreateRealClientSessionWithQueue(pm, jq, sessionId:1, connected: connected);
			return (pm, session);
		}

		// 검증1 : SYSTEM Path
		[Fact]
		public void RouteIncoming_SystemPacket_ReturnsSystemRouteWithJob()
		{
			var (pm, session) = Build( connected: true );   // State = Connected (C_EnterGame 허용)
			var buffer = MockFactoryHelper.SerializeClientPacket(PacketID.C_EnterGame, new C_EnterGame());

			PacketRoute route = pm.RouteIncoming(session, buffer);

			Assert.False( route.Dropped );
			Assert.Equal( PacketCategory.System, route.Category );
			Assert.NotNull( route.Job );
		}

		// 검증2 : SM-1 상태 필터 / Room 없음 드랍
		[Theory]
		[InlineData(PacketID.C_Move)]				// ROOM 패킷인데 COnnected 상태 - 상태 필터 드랍
		[InlineData(PacketID.C_ChangeRoom)]			// SYSTEM 패킷인데 InRoom에서만 허용 - 드랍
		public void RouteIncoming_DisallowedState_Drops(PacketID packetID)
		{
			var (pm, session) = Build( connected: true );       // Connected
			IMessage body = packetID == PacketID.C_Move
				? new C_Move {PosInfo = new PosInfo() }
				: new C_ChangeRoom();
			var buffer = MockFactoryHelper.SerializeClientPacket(packetID, body);

			PacketRoute route = pm.RouteIncoming(session, buffer);

			Assert.True( route.Dropped );
		}


	}
}
