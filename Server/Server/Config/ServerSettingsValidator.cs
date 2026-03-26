using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server.Config
{
	/// <summary>
	/// 통합된 서버 설정 검증자
	/// </summary>
	public class ServerSettingsValidator : IValidateOptions<ServerSettings>
	{
		public ValidateOptionsResult Validate( string name, ServerSettings options )
		{
			if(options == null)
				return ValidateOptionsResult.Fail( "ServerSettings cannot be null" );

			List<string> failures = new List<string>();

			// 네트워크 설정 검증
			ValidateNetwork( options.Network, failures );

			// 게임 데이터 설정 검증
			ValidateGameData( options.GameData, failures );

			// Job Queue 설정 검증
			ValidateJobQueue( options.JobQueue, failures );

			// Room 설정 검증
			ValidateRoom( options.Room, failures );

			// Redis 설정 검증
			ValidateRedis( options.Redis, failures );

			// DB 설정 검증
			ValidateDatabase( options.Database, failures );

			// Session 설정 검증
			ValidateSession( options.Session, failures );

			// Tick 설정 검증 (BaseTickMs + 구독 interval 교차 검증)
			ValidateTick( options.Tick, options.Room, options.Session, failures );

			return failures.Count == 0
				? ValidateOptionsResult.Success
				: ValidateOptionsResult.Fail( failures );
		}

		private void ValidateNetwork( NetworkConfig network, List<string> failures )
		{
			if(network == null)
			{
				failures.Add( "network configuration is required" );
				return;
			}

			if(string.IsNullOrWhiteSpace( network.Host ))
				failures.Add( "Network Host is required" );
			else if(!IPAddress.TryParse( network.Host, out _ ))
				failures.Add( $"Invalid Network Host IP address: {network.Host}" );

			if(network.Port < 1024 || 65535 < network.Port)
				failures.Add( $"Network Port must be between 1024 and 65535, got: {network.Port}" );

			if(network.ListenBacklog < 1|| 1000 < network.ListenBacklog)
				failures.Add( $"Network ListenBackLog must be between 1 and 1000, got : {network.ListenBacklog}" );
		}

		private void ValidateGameData( GameDataConfig gameData, List<string> failures )
		{
			if(gameData == null)
			{
				failures.Add( "GameData configuration is required" );
				return;
			}

			if(string.IsNullOrWhiteSpace( gameData.DataPath ))
				failures.Add( "GameData DataPath is required" );
		}

		private void ValidateJobQueue( JobQueueConfig jobQueue, List<string> failures )
		{
			if(jobQueue == null)
			{
				failures.Add( "JobQueue configuration is required" );
				return;
			}

			if(jobQueue.WorkerThreadCount < 1 || 32 < jobQueue.WorkerThreadCount)
				failures.Add( $"JobQueue WorkerThreadCount must be between 1 and 32, got: {jobQueue.WorkerThreadCount}" );

			if(jobQueue.MaxQueueSize < 100 || 100000 < jobQueue.MaxQueueSize)
				failures.Add( $"JobQueue MaxQueueSize must be between 100 and 10000, get: {jobQueue.MaxQueueSize}" );
		}

		private void ValidateRoom( RoomConfig room, List<string> failures )
		{
			if(room == null)
			{
				failures.Add( "Room configuration is required" );
				return;
			}

			if(room.MaxRooms < 1 || 1000 < room.MaxRooms)
				failures.Add( $"Room MaxRooms must be between 1 and 1000, got: {room.MaxRooms}" );

			if(room.Lobby?.MaxPlayers < 1 || 1000 < room.Lobby.MaxPlayers)
				failures.Add( $"Room Lobby MaxPlayers must be between 1 and 1000, got: {room.Lobby?.MaxPlayers}" );

			if(string.IsNullOrWhiteSpace( room.Lobby?.DefaultName ))
				failures.Add( "Room Lobby DefaultName is required" );

			if(room.TickIntervalMs < 16 || 1000 < room.TickIntervalMs)
				failures.Add( $"Room TickIntervalMs must be between 16 and 1000, got: {room.TickIntervalMs}" );
		}

		private void ValidateRedis( RedisConfig redis, List<string> failures )
		{
			if(redis == null)
			{
				failures.Add( "Redis configuration is required" );
				return;
			}

			if(string.IsNullOrWhiteSpace( redis.ConnectionString ))
				failures.Add( "Redis ConnectionString is required" );
		}

		private void ValidateDatabase( DatabaseConfig database, List<string> failures )
		{
			if(database == null)
			{
				failures.Add( "Database configuration is required" );
				return;
			}

			if(string.IsNullOrWhiteSpace( database.ConnectionString ))
				failures.Add( "Database ConnectionString is required" );
		}

		private void ValidateSession( SessionConfig session, List<string> failures )
		{
			if(session == null)
			{
				failures.Add( "Session configuration is required" );
				return;
			}

			if(session.HeartbeatIntervalMs < 1000 || 120000 < session.HeartbeatIntervalMs)
				failures.Add( $"Session HeartbeatIntervalMs must be between 1000 and 120000, got: {session.HeartbeatIntervalMs}" );

			if(session.TimeoutMs < 5000 || 600000 < session.TimeoutMs)
				failures.Add( $"Session TimeoutMs must be between 5000 and 600000, got: {session.TimeoutMs}" );

			if(session.TimeoutMs <= session.HeartbeatIntervalMs)
				failures.Add( $"Session TimeoutMs ({session.TimeoutMs}) must be greater than HeartbeatIntervalMs ({session.HeartbeatIntervalMs})" );
		}

		private void ValidateTick( TickConfig tick, RoomConfig room, SessionConfig session, List<string> failures )
		{
			if(tick == null)
			{
				failures.Add( "Tick configuration is required" );
				return;
			}

			if(tick.BaseTickMs < 16 || 1000 < tick.BaseTickMs)
				failures.Add( $"Tick BaseTickMs must be between 16 and 1000, get: {tick.BaseTickMs}" );

			// 교차검증: 구독 interval이 BaseTickMs 이상인지 확인
			// Register() 시점에도 검증하지만, 설정 로딩 시 미리 잡아내면 원인 파악이 빠름
			if(room != null && room.TickIntervalMs < tick.BaseTickMs)
				failures.Add( $"Room.TickIntervalMs ({room.TickIntervalMs}) must be >= Tick.BaseTickMs ({tick.BaseTickMs})" );

			if(session != null && session.HeartbeatIntervalMs < tick.BaseTickMs)
				failures.Add( $"Session.HeartbeatIntervalMs ({session.HeartbeatIntervalMs}) must be  >= Tick.BaseTickMs ({tick.BaseTickMs})" );
		}

	}
}
