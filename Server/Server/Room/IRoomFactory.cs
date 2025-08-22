using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room
{
	public interface IRoomFactory
	{
		IRoom CreateRoom( RoomType roomType, string roomName, int maxPlayers, bool isDefault = false );
	}

	public class RoomFactory : IRoomFactory
	{
		private readonly ILoggerFactory _loggerFactory;
		private readonly IOptions<RoomSettings> _roomSettings;

		public RoomFactory(ILoggerFactory loggerFactory, IOptions<RoomSettings> roomSettings )
		{
			_loggerFactory=loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			_roomSettings=roomSettings ?? throw new ArgumentNullException(nameof(roomSettings));
		}

		public IRoom CreateRoom (RoomType roomType, string roomName, int maxPlayers, bool isDefault = false )
		{
			return roomType switch
			{
				RoomType.Lobby => new LobbyRoom(
						_loggerFactory.CreateLogger<LobbyRoom>(),
						_roomSettings,
						roomName, false),
				RoomType.Battle => throw new NotImplementedException("BattleRoom not implemented yet"),
				RoomType.Dungeon => throw new NotImplementedException("DungeonRoom not implemented yet"),
				RoomType.Guild => throw new NotImplementedException("GuildRoom not implemented yet"),
				RoomType.Private => throw new NotImplementedException("PrivateRoom not implemented yet"),
				_ => throw new ArgumentException($"Unknown room type: {roomType}")
			};
		}
	}
}
