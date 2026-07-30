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
using Server.Services;
using Server.Services.Combat;
using Server.Services.Reward;
using Server.Packet.Handlers;
using Server.Infra.HealthCheck;
using DatabaseLib;
using DatabaseLib.Extensions;

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

			// DB 라이브러리를 이용해 DB / Redis / 일부 서비스 등록
			services.AddDatabaseLib( configuration );

			// 게임 로직 서비스 등록
			services.AddGameServices();

			// 데이터 서비스 등록
			services.AddDataServices();

			// 정적 데이터 서비스 등록
			services.AddStaticDataServices();

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

			// QueueManager 등록
			services.AddSingleton<IJobQueueManager, JobQueueManager>();

			// tickService 등록
			services.AddSingleton<TickService>();

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

			// Room Transition Coordinator 등록
			services.AddSingleton<IRoomTransitionCoordinator, RoomTransitionCoordinator>();

			// Service 등록
			services.AddSingleton<ICombatService, CombatService>();
			services.AddSingleton<IRewardService, RewardService>();
			services.AddSingleton<IPlayerPositionService, PlayerPositionService>();			// 내부에서 Redis 사용

			return services;
		}

		/// <summary>
		/// 데이터 관리 서비스 등록(DB, Redis 캐싱, 데이터 관리 등)
		/// </summary>
		private static IServiceCollection AddDataServices( this IServiceCollection services )
		{
			// 게임 데이터 서비스 등록 - db / redis 캐싱 / 데이터 관리
			services.AddSingleton<IGameDataService, GameDataService>();

			return services;
		}

		private static IServiceCollection AddStaticDataServices( this IServiceCollection services )
		{
			// 기본 데이터 관리
			services.AddSingleton<IDataManager, DataManager>();

			return services;
		}
	}
}
