using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server.Config
{
	public class ServerSettings
	{
		/// <summary>
		/// 네트워크 설정
		/// </summary>
		public NetworkConfig Network { get; set; } = new();

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

		/// <summary>
		/// Redis 설정
		/// </summary>
		public RedisConfig Redis { get; set; } = new();

		/// <summary>
		/// DB 설정
		/// </summary>
		public DatabaseConfig Database { get; set; } = new();
	}

	/// <summary>
	/// 네트워크 설정
	/// </summary>
	public class NetworkConfig
	{
		[Required]
		public string Host { get; set; } = "127.0.0.1";

		[Required]
		[Range( 1024, 65535 )]
		public int Port { get; set; } = 7777;

		[Range( 1, 1000 )]
		public int ListenBacklog { get; set; } = 10;
	}

	/// <summary>
	/// 게임 데이터 설정
	/// </summary>
	public class GameDataConfig
	{
		public string DataPath { get; set; } = "GameData";

		// 파일 경로 동적 생성 메서드
		public string GetDataFilePath( string tableName ) => 
			Path.Combine( DataPath, $"{tableName}.json" );
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
		[Range( 1, 10000 )]
		public int MaxRooms { get; set; } = 100;

		[Range( 3, 100 )]
		public int MaxRoomNameLength { get; set; } = 50;

		[Range( 5, 1440 )]
		public int EmptyRoomCleanupIntervalMinutes { get; set; } = 30;

		public LobbyConfig Lobby { get; set; } = new();
		public BattleConfig Battle { get; set; } = new();
		public DungeonConfig Dungeon { get; set; } = new();
		public GuildConfig Guild { get; set; } = new();
		public PrivateConfig Private { get; set; } = new();
	}

	public class LobbyConfig
	{
		[Range( 1, 1000 )]
		public int MaxPlayers { get; set; } = 100;

		public string DefaultName { get; set; } = "Main Lobby";
	}

	public class BattleConfig
	{
		[Range( 1, 100 )]
		public int MaxPlayers { get; set; } = 20;

		public string DefaultName { get; set; } = "Main Battle";
	}

	public class DungeonConfig
	{
		[Range( 1, 10 )]
		public int MaxPlayers { get; set; } = 4;

		public string DefaultName { get; set; } = "Main Dungeon";
	}

	public class GuildConfig
	{
		[Range( 1, 100 )]
		public int MaxPlayers { get; set; } = 50;

		public string DefaultName { get; set; } = "Main Guild";
	}

	public class PrivateConfig
	{
		[Range( 1, 10 )]
		public int MaxPlayers { get; set; } = 5;

		public string DefaultName { get; set; } = "Main Private";
	}

	public class RedisConfig
	{
		[Required]
		public string ConnectionString { get; set; }
	}

	public class DatabaseConfig
	{
		[Required]
		public string ConnectionString { get; set; }

		// Connection Pool 설정
		public int MinPoolSize { get; set; } = 10;					// 기본 연결 유지
		public int MaxPoolSize { get; set; } = 100;					// 동접 1000명 기준 10:1 비율
		public int ConnectionTimeout { get; set; } = 30;			// 연결 타임아웃 (초)
		public int CommandTimeout { get; set; } = 15;				// 쿼리 타임아웃 (초)
		public int MaxRetryOnFailure { get; set; } = 3;				// 재시도 횟수
		public bool EnableRetryOnFailure { get; set; } = true;
		public bool EnableSensitiveDataLogging { get; set; } = false;
	}
}
