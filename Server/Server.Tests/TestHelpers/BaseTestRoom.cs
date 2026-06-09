using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Data;
using Server.Room;
using Server.Services;
using Server.Services.Combat;
using Server.Services.Reward;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.TestHelpers
{
	/// <summary>
	/// Coordinator 통합 테스트용 BaseRoom 서브클래스
	/// J-2 cancellation 시나리오 검증에서 Mock<IRoom>의 cast 실패 우회를 위해 사용
	/// 실제 wiring은 Mock 의존성으로 최소화
	/// </summary>
	public class BaseTestRoom : BaseRoom
	{
		public BaseTestRoom(int roomId, int maxPlayers, IJobQueueManager jobQueueManager, string roomName = "Test", int mapId = 1)
			:base(
				 logger: NullLogger<BaseTestRoom>.Instance,
				 loggerFactory: new NullLoggerFactory(),
				 roomId: roomId,
				 roomName: roomName,
				 maxPlayers: maxPlayers,
				 dataManager: new Mock<IDataManager>().Object,
				 jobQueueManager: jobQueueManager,
				 combatService: new Mock<ICombatService>().Object,
				 rewardService: new Mock<IRewardService>().Object,
				 playerPositionService: new Mock<IPlayerPositionService>().Object,
				 monsterManagerFactory: null,
				 mapId: mapId)
		{

		}

		public override RoomType RoomType => RoomType.Lobby;

		// Task 반환 - BaseRoom.cs:544의 protected abstract Task OnInitPlayerPosition(IClientSession session) 메서드 구현
		protected override Task OnInitPlayerPosition( IClientSession session ) => Task.CompletedTask;
	}
}
