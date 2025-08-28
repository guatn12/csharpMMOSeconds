using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server.Configuration
{
	public class ServerSettings
	{
		/// <summary>
		/// 네트워크 설정
		/// </summary>
		public NetworkConfig Network { get; set; } = new();

		/// <summary>
		/// 로깅 설정
		/// </summary>
		public LoggingConfig Logging { get; set; } = new();

		/// <summary>
		/// 게임 데이터 설정
		/// </summary>
		public GameDataConfig GameData { get; set; } = new();

		/// <summary>
		/// Job Queue 설정
		/// </summary>
		public JobQueueConfig JobQueue { get; set; } = new();

		/// <summary>
		/// Room 설정
		/// </summary>
		public RoomConfig Room { get; set; } = new();
	}

	/// <summary>
	/// 네트워크 설정
	/// </summary>
	public class NetworkConfig
	{
		[Required]
		public string Host { get; set; } = "127.0.0.1";

		[Required]
		[Range( 1024, 65535)]
		public int Port { get; set; } = 7777;

		[Range( 1, 1000 )]
		public int ListenBacklog { get; set; } = 10;
	}

	/// <summary>
	/// 로깅 설정
	/// </summary>
	public class LoggingConfig
	{
		public string Level { get; set; } = "Information";
		public bool EnableConsole { get; set; } = true;
		public bool EnableFile { get; set; } = true;
		public string FilePath { get; set; } = "logs/server.log";
	}

	/// <summary>
	/// 게임 데이터 설정
	/// </summary>
	public class GameDataConfig
	{
		public string DataPath { get; set; } = "GameData";
		//public string FileExtension { get; set; } = ".json";
		public bool EnableHotReload { get; set; } = false;
		//public int HotReloadDebounceMs { get; set; } = 500;

		// 파일 경로 동적 생성 메서드
		//public string GetDataFilePath( string tableName ) =>
			//Path.Combine( DataPath, $"{tableName}{FileExtension}" );
	}

	/// <summary>
	/// Job Queue 설정
	/// </summary>
	public class JobQueueConfig
	{
		[Range( 1, 32 )]
		public int WorkerThreadCount { get; set; } = 4;

		[Range( 100, 100000 )]
		public int MaxQueueSize { get; set; } = 10000;

		public int QueueTimeoutMs { get; set; } = 5000;
	}

	/// <summary>
	/// Room 설정
	/// </summary>
	public class RoomConfig
	{
		[Range(1, 1000)]
		public int MaxRooms { get; set; } = 100;

		public LobbyConfig Lobby { get; set; } = new();
	}

	public class LobbyConfig
	{
		[Range( 1, 1000 )]
		public int MaxPlayers { get; set; } = 100;

		public string DefaultName { get; set; } = "Main Lobby";
	}
}
