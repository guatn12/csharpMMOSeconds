using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Protocol;
using Server.Config;
using Server.Data;
using Server.Data.Models;
using Server.Room;
using Server.Services;
using Server.Services.Combat;
using Server.Services.Reward;
using ServerCore;

namespace Server.Tests.TestHelpers
{
	/// <summary>
	/// 통합 테스트용 헬퍼 클래스
	/// 실제 LobbyRoom을 생성하여 테스트에 사용
	/// </summary>
	public static class IntegrationTestHelper
	{
		/// <summary>
		/// 단순 빌더 - new LobbyRoom + 의존성 주입만 수행
		/// State는 Created로 남고, MOnsterManager는 stub, JobQueue 미등록.
		/// 11-3 (1.5차 가드 검증) 전용 - 가드 진입 즉시 return하므로 위 항목들 무관.
		/// </summary>
		public static (LobbyRoom room, JobQueueManager jobQueueManager) BuildLobbyRoom(int roomId = 1, string roomName = "TestLobby", int maxPlayers = 4)
		{
			var serverSettings = BuildServerSettings(roomName, maxPlayers);
			var loggerFactory = LoggerFactory.Create(b => { });

			var mockDataManager = new Mock<IDataManager>();
			mockDataManager.Setup( d => d.GameConfig ).Returns( new GameConfigData
			{
				ViewDistance = 10,
				// GameConfigData의 다른 필드들도 필요한 경우 여기에 추가
			} );

			mockDataManager.Setup( d => d.GetMap( It.IsAny<int>() ) ).Returns( MapData.CreateEmpty(10, 10) );
			mockDataManager.Setup( d => d.IsDataLoaded ).Returns( true );
			mockDataManager.Setup( d => d.GetAllMonsters() ).Returns( new Dictionary<int, MonsterData>
			{
				//{ 1, new MonsterData
				//	{
				//		Id = 1,
				//		Name = "TestMonster",
				//		Health = 100,
				//		Attack = 10,
				//		Defense = 5,
				//		ExpReward = 50,
				//		GoldReward = 20,
				//		MoveSpeed = 2.0f,
				//		MonsterType = "Normal",
				//		Level = 1,
				//		Skills = new List<int>(),
				//		DetectRange = 5.0f,
				//		AttackRange = 1.5f,
				//		ChaseRange = 10.0f,
				//		ReturnRange = 15.0f,
				//		PatrolRadius = 3.0f,
				//		RespawnTime = 30.0f
				//	} 
				//}
			} );


			var mockPositionService = new Mock<IPlayerPositionService>();
			mockPositionService.Setup( s => s.UpdatePositionAsync( It.IsAny<long>(), It.IsAny<PosInfo>() ) ).Returns( Task.CompletedTask );
			mockPositionService.Setup( s => s.RemovePositionAsync( It.IsAny<long>() ) ).Returns( Task.CompletedTask );

			var combatService = new Mock<ICombatService>().Object;
			var rewardService = new Mock<IRewardService>().Object;

			// JobQueueManager 인스턴스만 생성 - Start 호출 안함
			var jobQueueManager = new JobQueueManager(loggerFactory.CreateLogger<JobQueueManager>());

			var room = new LobbyRoom(
				loggerFactory.CreateLogger<LobbyRoom>(),
				loggerFactory,
				serverSettings,
				mockDataManager.Object,
				jobQueueManager,
				combatService,
				rewardService,
				mockPositionService.Object,
				roomId,
				isDefaultLobby: true );

			return (room, jobQueueManager);
		}

		/// <summary>
		/// 단순 빌더 - new DungeonRoom + 의존성 주입만 수행
		/// State는 Created로 남고, MonsterManager는 stub, JobQueue 미등록
		/// 가드 검증 전용 - 가드 진입 즉시 return하므로 위 항목들 무관
		/// </summary>
		public static (DungeonRoom room, JobQueueManager jobQueueManager) BuildDungeonRoom( int roomId = 1, string roomName = "TestDungeon", int maxPlayers = 4 )
		{
			var serverSettings = BuildServerSettings(roomName, maxPlayers);
			var loggerFactory = LoggerFactory.Create(b => { });
			var mockDataManager = new Mock<IDataManager>();
			mockDataManager.Setup( d => d.GameConfig ).Returns( new GameConfigData
			{
				ViewDistance = 10,
				// GameConfigData의 다른 필드들도 필요한 경우 여기에 추가
			} );
			mockDataManager.Setup( d => d.GetMap( It.IsAny<int>() ) ).Returns( MapData.CreateEmpty(10, 10) );
			mockDataManager.Setup( d => d.IsDataLoaded ).Returns( true );
			mockDataManager.Setup(d => d.GetAllMonsters()).Returns( new Dictionary<int, MonsterData>
			{
				//{ 1, new MonsterData
				//	{
				//		Id = 1,
				//		Name = "TestMonster",
				//		Health = 100,
				//		Attack = 10,
				//		Defense = 5,
				//		ExpReward = 50,
				//		GoldReward = 20,
				//		MoveSpeed = 2.0f,
				//		MonsterType = "Normal",
				//		Level = 1,
				//		Skills = new List<int>(),
				//		DetectRange = 5.0f,
				//		AttackRange = 1.5f,
				//		ChaseRange = 10.0f,
				//		ReturnRange = 15.0f,
				//		PatrolRadius = 3.0f,
				//		RespawnTime = 30.0f
				//	} 
				//}
			} );

			var mockPositionService = new Mock<IPlayerPositionService>();
			mockPositionService.Setup( s => s.UpdatePositionAsync( It.IsAny<long>(), It.IsAny<PosInfo>() ) ).Returns( Task.CompletedTask );
			mockPositionService.Setup( s => s.RemovePositionAsync( It.IsAny<long>() ) ).Returns( Task.CompletedTask );
			var combatService = new Mock<ICombatService>().Object;
			var rewardService = new Mock<IRewardService>().Object;
			// JobQueueManager 인스턴스만 생성 - Start 호출 안함
			var jobQueueManager = new JobQueueManager(loggerFactory.CreateLogger<JobQueueManager>());
			var room = new DungeonRoom(
				loggerFactory.CreateLogger<DungeonRoom>(),
				loggerFactory,
				serverSettings,
				mockDataManager.Object,
				jobQueueManager,
				combatService,
				rewardService,
				mockPositionService.Object,
				roomId,
				roomName,
				maxPlayers);

			return (room, jobQueueManager);
		}

		/// <summary>
		/// 충실도 빌더 - production의 RoomManager.CreateDefaultLobbyAsync 핵심 단계 재현:
		///		- new LobbyRoom + 의존성
		///		- InitializeAsync() -> State: Created -> active + monstermanager 초기화
		///		- JobQueueManager.Start + RegisterAsync(room) -> worker가 이 room의 job을 dispatch 가능
		///	11-4a/4b 전용 - EnterViaQueueAsync/LeaveViaQueueAsync 실효 검증 필요.
		/// </summary>
		public static async Task<(LobbyRoom room, JobQueueManager jobQueueManager)> BuildAndRegisterLobbyRoomAsync( int roomId = 1, string roomName = "TestLobby", 
			int maxPlayers = 4 )
		{
			var loggerFactory = LoggerFactory.Create(b => { });
			var (room, jobQueueManager) = BuildLobbyRoom(roomId, roomName, maxPlayers);

			jobQueueManager.Start(workerCount: 1);
			await room.InitializeAsync();
			await jobQueueManager.RegisterAsync(room);

			return (room, jobQueueManager);
		}

		/// <summary>
		/// 충실도 빌더 - production의 RoomManager.CreateRoomAsync(Dungeon) 핵심 단계 재현: 
		///			- new DungeonRoom + 의존성
		///			- InitializeAsync() -> State: Created -> active 초기화
		///			- JobQueueManager.Start + RegisterAsync(room) -> worker가 이 room의 job을 dispatch 가능
		///	통합 테스트 전용 - EnterViaQueueAsync/LeaveViaQueueAsync 실효 검증 필요.
		/// </summary>
		public static async Task<(DungeonRoom room, JobQueueManager jobQueueManager)> BuildAndRegisterDungeonRoomAsync
			(int roomId = 1, string roomName = "TestDungeon", int maxPlayers = 4)
		{
			var (room, jobQueueManager) = BuildDungeonRoom( roomId, roomName, maxPlayers );

			jobQueueManager.Start( workerCount: 1 );
			await room.InitializeAsync();
			await jobQueueManager.RegisterAsync( room );

			return (room, jobQueueManager);
		}

		private static IOptions<ServerSettings> BuildServerSettings( string roomName, int maxPlayers )
			=> Options.Create( new ServerSettings
			{
				Room = new RoomConfig
				{
					Lobby   = new LobbyConfig { DefaultName = roomName, MaxPlayers = maxPlayers },
					Dungeon = new DungeonConfig { DefaultName = "d", MaxPlayers = maxPlayers },
					Battle  = new BattleConfig { DefaultName = "b", MaxPlayers = maxPlayers },
					Guild   = new GuildConfig { DefaultName = "g", MaxPlayers = maxPlayers },
					Private = new PrivateConfig { DefaultName = "p", MaxPlayers = maxPlayers },
					EmptyRoomCleanupIntervalMinutes = 0,
					MaxRoomNameLength = 255,
					MaxRooms = 100,
					TickIntervalMs = 100,
				},
				Tick = new TickConfig { BaseTickMs = 100 },
			} );
	}
}
