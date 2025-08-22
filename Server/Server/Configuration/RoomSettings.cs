using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Configuration
{
	public class RoomSettings
	{
		public DefaultRoomSettings Default { get; set; } = new DefaultRoomSettings();

		public LobbyRoomSettings Lobby { get; set; } = new LobbyRoomSettings();

		public BattleRoomSettings Battle { get; set; } = new BattleRoomSettings();

		public CleanupSettings Cleanup { get; set; } = new CleanupSettings();

		public PerformanceSettings Performance { get; set; } = new PerformanceSettings();
	}

	public class DefaultRoomSettings
	{
		[Range( 1, 1000 )]
		public int MaxPlayers { get; set; } = 100;

		[Range( 1, 100 )]
		public int MaxRoomNameLength { get; set; } = 50;

		public bool AutoStart { get; set; } = true;
	}

	public class LobbyRoomSettings
	{
		[Range( 1, 10000 )]
		public int MaxPlayers { get; set; } = 1000;

		public string DefaultLobbyName { get; set; } = "Main Lobby";

		public bool AutoCreateLobby { get; set; } = true;

		public bool EnableWelcomMessage { get; set; } = true;

		public bool EnalbeJoinLeaveNotifications { get; set; } = true;

		public bool ShowLobbyStatus { get; set; } = true;

		[Range( 10, 500 )]
		public int MaxChatMessageLength { get; set; } = 200;
	}

	public class BattleRoomSettings
	{
		[Range( 2, 100 )]
		public int MaxPlayers { get; set; } = 20;

		public bool AutoMatching { get; set; } = true;

		[Range( 1, 120 )]
		public int TimeLimitMinutes { get; set; } = 30;
	}

	public class CleanupSettings
	{
		[Range( 1, 1440 )]
		public int EmptyRoomCleanupIntervalMinutes { get; set; } = 5;

		[Range( 0, 60 )]
		public int EmptyRoomGracePeriodMinutes { get; set; } = 2;

		public bool EnalbeAutoCleanup { get; set; } = true;
	}

	public class PerformanceSettings
	{
		[Range( 1, 10000 )]
		public int MaxConcurrentRooms { get; set; } = 1000;

		[Range( 100, 10000 )]
		public int MaxJobQueueSize { get; set; } = 1000;

		[Range( 1, 1000 )]
		public int BroadcastBatchSize { get; set; } = 50;
	}
}
