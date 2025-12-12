using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Config;
using Server.Core.Jobs;
using Server.Data;
using Server.Services;
using Server.Services.Combat;
using Server.Services.Reward;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room
{
	/// <summary>
	/// Room 생성을 담당하는 Factory 구현체
	/// </summary>
	public class RoomFactory : IRoomFactory
	{
		private readonly ILoggerFactory _loggerFactory;
		private readonly DataManager _dataManager;
		private readonly JobQueueManager _jobQueueManager;
		private readonly JobPool _jobPool;
		private readonly IOptions<ServerSettings> _serverSettings;

		public RoomFactory(ILoggerFactory loggerFactory, DataManager dataManager, JobQueueManager jobQueueManager, 
			JobPool jobPool, IOptions<ServerSettings> serverSettings )
		{
			_loggerFactory=loggerFactory;
			_dataManager=dataManager;
			_jobQueueManager=jobQueueManager;
			_jobPool=jobPool;
			_serverSettings=serverSettings;
		}

		public IRoom CreateRoom( RoomType roomType, string roomName, int maxPlayers, IServiceProvider serviceProvider )
		{
			var combatService = serviceProvider.GetRequiredService<ICombatService>();
			var rewardService = serviceProvider.GetRequiredService<IRewardService>();
			var playerPositionService = serviceProvider.GetRequiredService<PlayerPositionService>();

			return roomType switch
			{
				RoomType.Lobby => new LobbyRoom( _loggerFactory.CreateLogger<LobbyRoom>(), _serverSettings, _dataManager, 
				_jobQueueManager, _jobPool, combatService, rewardService, playerPositionService, roomName, 
				isDefaultLobby: false ),

				RoomType.Battle => throw new NotImplementedException( "BattleRoom not implemented yet"),
				RoomType.Dungeon => throw new NotImplementedException( "DungeonRoom not implemented yet"),
				RoomType.Guild => throw new NotImplementedException( "GuildRoom not implemented yet"),
				RoomType.Private => throw new NotImplementedException( "PrivateRoom not implemented yet"),
				_ => throw new ArgumentException( $"Unknown room type: {roomType}" )
			};
		}
	}
}
