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
using Server.Services.Persistence;
using Server.Room.Dependencies;
using Server.Core.Session;

namespace Server.Room
{
	/// <summary>
	/// Room 생성을 담당하는 Factory 구현체
	/// </summary>
	public class RoomFactory : IRoomFactory
	{
		private readonly ILoggerFactory _loggerFactory;
		private readonly IDataManager _dataManager;
		private readonly IJobQueueManager _jobQueueManager;
		private readonly IOptions<ServerSettings> _serverSettings;
		private readonly ISessionManager _sessionManager;

		public RoomFactory(ILoggerFactory loggerFactory, IDataManager dataManager, IJobQueueManager jobQueueManager, ISessionManager sessionManager, IOptions<ServerSettings> serverSettings )
		{
			_loggerFactory=loggerFactory;
			_dataManager=dataManager;
			_jobQueueManager=jobQueueManager;
			_sessionManager=sessionManager;
			_serverSettings=serverSettings;
		}

		public IRoom CreateRoom( RoomType roomType, int roomId, string roomName, int maxPlayers, IServiceProvider serviceProvider )
		{
			var roomServices = serviceProvider.GetRequiredService<RoomServices>();
			return roomType switch
			{
				RoomType.Lobby => new LobbyRoom( _loggerFactory.CreateLogger<LobbyRoom>(), _loggerFactory, _serverSettings, _sessionManager, _dataManager,
				_jobQueueManager, roomServices, roomId, roomName,
				isDefaultLobby: false ),

				RoomType.Battle => throw new NotImplementedException( "BattleRoom not implemented yet" ),
				RoomType.Dungeon => new DungeonRoom( _loggerFactory.CreateLogger<DungeonRoom>(), _loggerFactory, _sessionManager, _serverSettings, _dataManager,
				_jobQueueManager, roomServices, roomId, roomName, maxPlayers ),
				RoomType.Guild => throw new NotImplementedException( "GuildRoom not implemented yet" ),
				RoomType.Private => throw new NotImplementedException( "PrivateRoom not implemented yet" ),
				_ => throw new ArgumentException( $"Unknown room type: {roomType}" )
			};
		}
	}
}
