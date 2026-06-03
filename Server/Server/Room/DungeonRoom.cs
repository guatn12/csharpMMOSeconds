using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Protocol;
using Server.Config;
using Server.Core.Session;
using Server.Data;
using Server.Game.Monsters;
using Server.Services;
using Server.Services.Combat;
using Server.Services.Reward;
using ServerCore;
using System;
using System.Threading.Tasks;

namespace Server.Room
{
	public class DungeonRoom : BaseRoom
	{
		private const int DUNGEON_MAP_ID = 3;
		private readonly ServerSettings _settings;
		public override RoomType RoomType => RoomType.Dungeon;

		public DungeonRoom(ILogger<DungeonRoom> logger, ILoggerFactory loggerFactory, IOptions<ServerSettings> settings, 
			IDataManager dataManager, IJobQueueManager jobQueueManager, ICombatService combatService, IRewardService rewardService,
			IPlayerPositionService playerPositionService, int roomId, string roomName, int maxPlayers)
			: base(logger, loggerFactory, roomId, roomName, maxPlayers, dataManager, jobQueueManager, combatService, rewardService, 
				  playerPositionService, mapId: DUNGEON_MAP_ID)
		{
			_settings = settings.Value;
		}

		protected override Task<RoomEnterResult> TryEnterAsync( IClientSession session, bool consumesReservation = false )
		{
			// 던전 전용 입장 조건 검증(level, 인원 제한 등)
			// ex) if(session.Player.Level < datamanager.dungeon.getlevel(DUNGEON_MAP_ID)) 

			return base.TryEnterAsync( session, consumesReservation );
		}

		protected override async Task OnPlayerEnterAsync(IClientSession session )
		{
			// TODO: 추가로 전송해야 할 내용이 있을 경우 여기에 작성 (던전 입장 시 필요한 정보 등)
			await base.OnPlayerEnterAsync( session );
		}

		protected override async Task OnInitPlayerPosition( IClientSession session )
		{
			// 방 A 중앙 좌표 (14.0, 0.0, 14.0)로 초기 위치 설정
			var startPos = new PosInfo{PosX = 14.0f, PosY = 0.0f, PosZ = 14.0f };

			session.Player.InitPosition( startPos );
			RoomMap.Add( session.Player, startPos.PosX, startPos.PosZ );
			await _playerPositionService.UpdatePositionAsync( session.PlayerId, startPos );

			_logger.LogInformation( "Player {Id} spawned at Dungeon entrance (RoomA)", session.PlayerId );
		}

		protected override void SetupDefaultSpawnPoints()
		{
			// 방 C
			MonsterManager.AddSpawnPoint( 2201, new PosInfo { PosX = 50.0f, PosY = 0.0f, PosZ = 14.0f } ); // 슬라임
			MonsterManager.AddSpawnPoint( 2003, new PosInfo { PosX = 46.0f, PosY = 0.0f, PosZ = 18.0f } ); // 들쥐

			// 방B
			MonsterManager.AddSpawnPoint( 2201, new PosInfo { PosX = 14.0f, PosY = 0.0f, PosZ = 50.0f } ); // 슬라임
			MonsterManager.AddSpawnPoint( 2002, new PosInfo { PosX = 18.0f, PosY = 0.0f, PosZ = 46.0f } ); // 고블린전사

			// 방D
			MonsterManager.AddSpawnPoint( 2101, new PosInfo { PosX = 50.0f, PosY = 0.0f, PosZ = 50.0f } );	// 오크대장
		}

		protected override MonsterSpawnPolicy GetMonsterSpawnPolicy()
		{
			return MonsterSpawnPolicy.BattleDefault;
		}
	}
}
