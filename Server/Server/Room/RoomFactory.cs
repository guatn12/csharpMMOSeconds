using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Config;
using Server.Data;
using Server.Services;
using Server.Services.Combat;
using Server.Services.Reward;
using ServerCore;
using System;
using Protocol;

namespace Server.Room
{
	/// <summary>
	/// Room 생성을 담당하는 Factory 구현체
	/// </summary>
	public class RoomFactory : IRoomFactory
	{
		private readonly ILoggerFactory _loggerFactory;
		private readonly DataManager _dataManager;
		private readonly IJobQueueManager _jobQueueManager;
		private readonly IOptions<ServerSettings> _serverSettings;

		public RoomFactory(ILoggerFactory loggerFactory, DataManager dataManager, IJobQueueManager jobQueueManager, IOptions<ServerSettings> serverSettings )
		{
			_loggerFactory=loggerFactory;
			_dataManager=dataManager;
			_jobQueueManager=jobQueueManager;
			_serverSettings=serverSettings;
		}

		public IRoom CreateRoom( RoomType roomType, int roomId, string roomName, int maxPlayers, IServiceProvider serviceProvider )
		{
			var combatService = serviceProvider.GetRequiredService<ICombatService>();
			var rewardService = serviceProvider.GetRequiredService<IRewardService>();
			var playerPositionService = serviceProvider.GetRequiredService<PlayerPositionService>();

			return roomType switch
			{
				RoomType.Lobby => new LobbyRoom( _loggerFactory.CreateLogger<LobbyRoom>(), _loggerFactory, _serverSettings, _dataManager,
				_jobQueueManager, combatService, rewardService, playerPositionService, roomId, roomName,
				isDefaultLobby: false ),

				RoomType.Battle => throw new NotImplementedException( "BattleRoom not implemented yet" ),
				RoomType.Dungeon => new DungeonRoom( _loggerFactory.CreateLogger<DungeonRoom>(), _loggerFactory, _serverSettings, _dataManager,
				_jobQueueManager, combatService, rewardService, playerPositionService, roomId, roomName, maxPlayers ),
				RoomType.Guild => throw new NotImplementedException( "GuildRoom not implemented yet" ),
				RoomType.Private => throw new NotImplementedException( "PrivateRoom not implemented yet" ),
				_ => throw new ArgumentException( $"Unknown room type: {roomType}" )
			};
		}
	}
}
