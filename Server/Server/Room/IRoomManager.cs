using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room
{
	public interface IRoomManager
	{
		Task<IRoom> CreateRoomAsync( RoomType roomType, string roomName, int maxPlayers, GameSession creatorSession = null );
		Task<IRoom> CreateDefaultLobbyAsync();
		Task<bool> DestoryRoomAsync( int roomId );
		Task<int> CleanupEmptyRoomsAsync();

		Task<IRoom> FindRoomAsync( int roomId );
		Task<IRoom> FindRoomByNameAsync(string roomName );
		Task<IRoom> FindAvailableRoomAsync( RoomType roomType = RoomType.Lobby );
		Task<IReadOnlyList<IRoom>> GetRoomsByTypeAsync( RoomType roomType );
		IReadOnlyList<IRoom> GetActiveRooms();

		Task<RoomEnterResult> JoinDefaultLobbyAsync( GameSession session );
		Task<RoomEnterResult> MovePlayerToRoomAsync( GameSession session, int targetRoomId );
		Task<IRoom> FindPlayerCurrentRoomAsync( GameSession session );
		Task<bool> RemovePlayerFromAllRoomsAsync( GameSession session );

		int TotalRoomCount { get; }
		int TotalPlayerCount { get; }

		Task<Dictionary<RoomType, RoomStatistics>> GetRoomStatisticsAsync();

		RoomManagerMemoryInfo GetMemoryInfo();

		event EventHandler<RoomCreatedEventArgs> RoomCreated;
		event EventHandler<RoomDestoryedEventArgs> RoomDestoryed;
		event EventHandler<PlayerRoomChangedEventArgs> PlayerRoomChanged;

		Task InitializeAsync();
		Task ShutdownAsync();
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
	}

	public class RoomCreatedEventArgs : EventArgs
	{
		public IRoom Room { get; }
		public GameSession Creator { get; }
		public DateTime CreatedAt { get; }

		public RoomCreatedEventArgs( IRoom room, GameSession creator)
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
		public GameSession Player { get; }
		public IRoom PreviousRoom { get; }
		public IRoom CurrentRoom { get; }
		public DateTime ChangedAt { get; }

		public PlayerRoomChangedEventArgs(GameSession player, IRoom preivousRoom, IRoom currentRoom)
		{
			Player = player ?? throw new ArgumentNullException(nameof(player));
			PreviousRoom = preivousRoom;	// null 허용 (첫 입장인 경우)
			CurrentRoom = currentRoom;		// null 허용 (퇴장인 경우)
			ChangedAt = DateTime.UtcNow;
		}
	}
}
