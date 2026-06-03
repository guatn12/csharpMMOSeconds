using Server.Core.Session;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Protocol;

namespace Server.Room
{
	public interface IRoomManager
	{
		Task<IRoom> CreateRoomAsync( RoomType roomType, string roomName, int maxPlayers, IClientSession creatorSession = null );
		Task<IRoom> CreateDefaultLobbyAsync();
		Task<bool> DestoryRoomAsync( int roomId );
		Task<int> CleanupEmptyRoomsAsync();

		Task<IRoom> FindRoomAsync( int roomId );
		IRoom FindRoom( int roomId );
		Task<IRoom> FindRoomByNameAsync(string roomName );
		Task<IRoom> FindAvailableRoomAsync( RoomType roomType = RoomType.Lobby );
		Task<IReadOnlyList<IRoom>> GetRoomsByTypeAsync( RoomType roomType );
		IReadOnlyList<IRoom> GetActiveRooms();

		Task<RoomEnterResult> JoinDefaultLobbyAsync( IClientSession session );
		Task<IRoom> FindPlayerCurrentRoomAsync( IClientSession session );
		Task<bool> RemovePlayerFromAllRoomsAsync( IClientSession session );

		int TotalRoomCount { get; }
		int TotalPlayerCount { get; }

		Task<Dictionary<RoomType, RoomStatistics>> GetRoomStatisticsAsync();

		RoomManagerMemoryInfo GetMemoryInfo();

		event EventHandler<RoomCreatedEventArgs> RoomCreated;
		event EventHandler<RoomDestoryedEventArgs> RoomDestoryed;
		event EventHandler<PlayerRoomChangedEventArgs> PlayerRoomChanged;

		Task StartAsync();
		Task StopAsync();
	}

	public class RoomStatistics
	{
		public int RoomCount { get; set; }
		public int PlayerCount { get; set; }
		public int AvailableRooms { get; set; }
		public int FullRooms { get; set; }
		public double AveragePlayersPerRoom { get; set; }
	}

	public class RoomManagerMemoryInfo
	{
		public long EstimatedMemoryUsage { get; set; }
		public int ActiveRoomCount { get; set; }
		public int TotalPlayerCount { get; set; }
		public Dictionary<RoomType, int> RoomCountByType { get; set; } = new Dictionary<RoomType, int>();
		public DateTime LastCleanupTime { get; set; }
	}

	public class RoomCreatedEventArgs : EventArgs
	{
		public IRoom Room { get; }
		public IClientSession Creator { get; }
		public DateTime CreatedAt { get; }

		public RoomCreatedEventArgs( IRoom room, IClientSession creator )
		{
			Room = room ?? throw new ArgumentNullException(nameof(room));
			Creator = creator;  // null 허용(시스템에서 생성한 경우)
			CreatedAt = DateTime.UtcNow;
		}
	}

	public class RoomDestoryedEventArgs : EventArgs
	{
		public int RoomId { get; }
		public string RoomName { get; }
		public RoomType RoomType { get; }
		public DateTime DestroyedAt { get; }
		public string Reason { get; }

		public RoomDestoryedEventArgs(int roomId, string roomName, RoomType roomType, string reason = "" )
		{
			RoomId=roomId;
			RoomName=roomName ?? "";
			RoomType=roomType;
			Reason=reason ?? "";
			DestroyedAt=DateTime.UtcNow;
		}
	}

	public class PlayerRoomChangedEventArgs : EventArgs
	{
		public IClientSession Player { get; }
		public IRoom PreviousRoom { get; }
		public IRoom CurrentRoom { get; }
		public DateTime ChangedAt { get; }

		public PlayerRoomChangedEventArgs( IClientSession player, IRoom preivousRoom, IRoom currentRoom)
		{
			Player = player ?? throw new ArgumentNullException(nameof(player));
			PreviousRoom = preivousRoom;	// null 허용 (첫 입장인 경우)
			CurrentRoom = currentRoom;		// null 허용 (퇴장인 경우)
			ChangedAt = DateTime.UtcNow;
		}
	}
}
