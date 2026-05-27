using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Protocol;
using Server.Config;
using Server.Core.Session;
using Server.Data;
using Server.Room;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Room
{
	public class RespawnPendingTests
	{
		private static DataManager CreateEmptyDataManager()
		{
			var settings = Options.Create(new ServerSettings
			{
				GameData = new GameDataConfig {DataPath = "" }
			});
			return new DataManager( settings, NullLogger<DataManager>.Instance );
		}

		private static ILoggerFactory CreateEmptyLoggerFactory() => LoggerFactory.Create( b => { } );

		// 테스트 전용 최소 BaseRoom - abstract 우회용
		// BaseRoom 생성자가 요구하는 종속성은 모두 null/mock으로 주입.
		// BeginEndRespawnPending + TryBeginClose만 호출하므로 다른 종속성 미사용.
		private class TestableRoom : BaseRoom
		{
			public TestableRoom( IJobQueueManager jq, DataManager dataManager, ILoggerFactory loggerFactory )
				: base(
					  logger: NullLogger.Instance,
					  loggerFactory: loggerFactory,
					  roomId: 1,
					  roomName: "test",
					  maxPlayers: 4,
					  dataManager: dataManager,
					  jobQueueManager: jq,
					  combatService: null,
					  rewardService: null,
					  playerPositionService: null )
			{ }

			public override RoomType RoomType => RoomType.Lobby;
			protected override Task OnInitPlayerPosition( IClientSession session ) => Task.CompletedTask;
			protected override void SetupDefaultSpawnPoints()
			{
			}
		}

		// 매번 새 인스턴스 생성(ResetStateForTest 불필요)
		private static TestableRoom CreateTestableRoom() => new TestableRoom( new JobQueueManager( NullLogger<JobQueueManager>.Instance ), CreateEmptyDataManager(),
			CreateEmptyLoggerFactory());

		[Fact]
		public void RespawnPending_BlocksRoomClose_AndReleases()
		{
			// 초기 빈 방 -> close 가능
			var room1 = CreateTestableRoom();
			Assert.True( room1.TryBeginClose() );

			// BeginRespawnPending -> close 차단
			var room2 = CreateTestableRoom();
			room2.BeginRespawnPending();
			Assert.False( room2.TryBeginClose() );

			// EndRespawnPending -> Close 가능 복귀
			var room3 = CreateTestableRoom();
			room3.BeginRespawnPending();
			room3.EndRespawnPending();
			Assert.True( room3.TryBeginClose() );
		}

		[Fact]
		public void EndRespawnPending_NoUnderflow_StillAllowsClose()
		{
			var room = CreateTestableRoom();
			// EndRespawnPending이 BeginRespawnPending 없이 호출되어도 상태가 이상해지지 않아야 함
			room.EndRespawnPending();
			Assert.True( room.TryBeginClose() ); // close 가능 -> 카운터가 음수가 되지 않았음을 간접적으로 검증
		}
	}
}
