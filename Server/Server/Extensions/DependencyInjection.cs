using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Server.Data;
using Server.Packet;
using Server.Room;
using ServerCore;
using Server.Config;
using Server.Core.Session;
using Server.Infra;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System;
using Npgsql;
using Server.Database.Services;
using Server.Services;
using Server.Services.Combat;
using Server.Services.Reward;
using Server.Packet.Handlers;
using Server.Infra.HealthCheck;

namespace Server.Extensions
{
	public static class DependencyInjection
	{
		///<summary>
		///모든 애플리케이션 서비스를 등록합니다.
		///</summary>
		public static IServiceCollection AddAppServices( this IServiceCollection services, IConfiguration configuration )
		{
			// 설정 시스템 등록
			services.AddConfigurationServices( configuration );

			// 핵심 서비스 등록
			services.AddCoreServices( configuration );

			// 게임 로직 서비스 등록
			services.AddGameServices();

			// 데이터 서비스 등록
			services.AddDataServices();

			// 레디스 서비스 등록
			services.AddRedisService( configuration );

			// Health Check 추가 (DB 연결 상태 모니터링)
			services.AddHealthChecks()
				.AddDbContextCheck<AppDbContext>( "database" )
				.AddCheck<RedisHealthCheck>( "redis" );

			services.AddSingleton<PerformanceMonitoringService>();
			services.AddHostedService(sp => sp.GetRequiredService<PerformanceMonitoringService>() );

			return services;
		}

		///<summary>
		///단순화된 설정 시스템 등록
		/// </summary>
		private static IServiceCollection AddConfigurationServices( this IServiceCollection services, IConfiguration configuration )
		{
			// 통합된 서버 설정 바인딩
			services.Configure<ServerSettings>( configuration.GetSection( "ServerSettings" ) );

			// 설정 검증자 등록 (1개로 통합)
			services.AddSingleton<IValidateOptions<ServerSettings>, ServerSettingsValidator>();

			return services;
		}

		///<summary>
		/// 핵심 네트워킹 서비스 등록
		/// </summary>
		private static IServiceCollection AddCoreServices( this IServiceCollection services, IConfiguration configuration )
		{
			// 네트워킹 핵심
			services.AddSingleton<Listener>();
			services.AddSingleton<SystemPacketHandler>();
			services.AddSingleton<PacketManager>();
			services.AddSingleton<ISessionManager, SessionManager>();

			// DB 서비스
			DatabaseConfig databaseConfig = configuration.GetSection("ServerSettings:Database").Get<DatabaseConfig>()
				?? throw new InvalidOperationException("Database 설정을 찾을 수 없습니다.");

			var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

			string connectionString = databaseConfig.ConnectionString;
			if(string.IsNullOrEmpty( connectionString ))
			{
				connectionString = configuration.GetConnectionString( "DefaultConnection" )
					?? throw new InvalidOperationException( "Database 연결 문자열을 찾을 수 없습니다." );
			}

			// Npgsql Connection Pool 최적화
			var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
			{
				MinPoolSize = databaseConfig.MinPoolSize,
				MaxPoolSize = databaseConfig.MaxPoolSize,
				ConnectionLifetime = databaseConfig.ConnectionTimeout,
				Timeout = databaseConfig.ConnectionTimeout,
				CommandTimeout = databaseConfig.CommandTimeout,

				// 추가 성능 최적화
				Pooling = true,
				IncludeErrorDetail = environment == "Development"
			};

			// DbContextFactory만 사용 (MMORPG 서버에서는 Scoped DbContext 불필요)
			services.AddDbContextFactory<AppDbContext>( options =>
			{
				options.UseNpgsql( connectionStringBuilder.ConnectionString, npgsqloptions =>
				{
					npgsqloptions.CommandTimeout( databaseConfig.CommandTimeout );
				} );

				if(environment == "Development" && databaseConfig.EnableSensitiveDataLogging)
				{
					options.EnableSensitiveDataLogging();
					options.EnableDetailedErrors();
				}

				options.EnableServiceProviderCaching();
			} );

			// QueueManager 등록
			services.AddSingleton<IJobQueueManager, JobQueueManager>();

			return services;
		}

		///<summary>
		/// 게임 로직 서비스 등록
		/// </summary>
		private static IServiceCollection AddGameServices( this IServiceCollection services )
		{
			// Room Factory 등록
			services.AddSingleton<IRoomFactory, RoomFactory>();

			// Room 시스템
			services.AddSingleton<IRoomManager, RoomManager>();

			// Service 등록
			services.AddSingleton<ICombatService, CombatService>();
			services.AddSingleton<IRewardService, RewardService>();

			return services;
		}

		/// <summary>
		/// 데이터 관리 서비스 등록
		/// </summary>
		private static IServiceCollection AddDataServices( this IServiceCollection services )
		{
			// 기본 데이터 관리
			services.AddSingleton<DataManager>();

			// 새 캐싱 서비스들 추가
			services.AddSingleton<PlayerCacheService>();
			services.AddSingleton<InventoryCacheService>();

			return services;
		}

		public static IServiceCollection AddRedisService(this IServiceCollection services, IConfiguration configuration )
		{
			// Redis 설정 가져오기
			var redisConnectionString = configuration.GetSection("ServerSettings:Redis:ConnectionString").Value;

			// ConnectionMultiplexer 등록
			services.AddSingleton<IConnectionMultiplexer>( provider =>
			{
				var configurationOptions = new ConfigurationOptions
				{
					EndPoints = {redisConnectionString},
					AbortOnConnectFail = false, // 연결 실패 시 서버 중단 방지
					ConnectTimeout = 5000,
					ConnectRetry = 3
				};

				return ConnectionMultiplexer.Connect( configurationOptions );
			} );

			// RedisService 등록
			services.AddSingleton<RedisService>();
			services.AddSingleton<PlayerPositionService>();


			return services;
		}
	}
}
