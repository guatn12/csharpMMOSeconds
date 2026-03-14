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
			DataManager dataManager, IJobQueueManager jobQueueManager, ICombatService combatService, IRewardService rewardService,
			PlayerPositionService playerPositionService, string roomName, int maxPlayers)
			: base(logger, loggerFactory, roomName, maxPlayers, dataManager, jobQueueManager, combatService, rewardService, 
				  playerPositionService, mapId: DUNGEON_MAP_ID)
		{
			_settings = settings.Value;
		}

		public override Task<RoomEnterResult> TryEnterAsync( IClientSession session )
		{
			// 던전 전용 입장 조건 검증(level, 인원 제한 등)
			// ex) if(session.Player.Level < datamanager.dungeon.getlevel(DUNGEON_MAP_ID)) 

			return base.TryEnterAsync( session );
		}

		protected override async Task OnPlayerEnterAsync(IClientSession session )
		{
			// 방 A 중앙 좌표 (14.0, 0.0, 14.0)로 초기 위치 설정
			var startPos = new PosInfo{PosX = 14.0f, PosY = 0.0f, PosZ = 14.0f };

			try
			{
				session.Player.InitPosition( startPos );
				RoomMap.Add( session.Player, startPos.PosX, startPos.PosZ );
				await _playerPositionService.UpdatePositionAsync( session.PlayerId, startPos );

				_logger.LogInformation( "Player {Id} spawned at Dungeon entrance (RoomA)", session.PlayerId );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to set spawn position for Player {Id}", session.PlayerId );
			}

			await base.OnPlayerEnterAsync( session );
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
