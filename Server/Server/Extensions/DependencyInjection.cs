using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Data;
using Server.Packet;
using Server.Room;
using ServerCore;
using Serilog;
using Server.Config;
using Server.Core.Session;
using Server.Core.Jobs;
using Server.Infra;
using Microsoft.EntityFrameworkCore;

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
			services.AddCoreServices(configuration);

			// 게임 로직 서비스 등록
			services.AddGameServices();

			// 데이터 서비스 등록
			services.AddDataServices();

			// 로깅 서비스 등록
			services.AddLoggingServices( configuration );

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
			services.AddSingleton<PacketManager>();

			// Job Queue 시스템
			services.AddSingleton<JobPool>();
			services.AddSingleton( JobQueueManager.Instance );

			// GameSession 팩토리
			services.AddTransient<GameSession>( provider =>
			{
				var logger = provider.GetRequiredService<ILogger<GameSession>>();
				var roomManager = provider.GetRequiredService<IRoomManager>();
				return new GameSession( logger, roomManager );
			} );

			// Redis 서비스
			services.AddSingleton<RedisService>();

			// DB 서비스
			string connectionString = configuration.GetSection("ServerSettings:Database:ConnectionString").Value;

			services.AddDbContext<AppDbContext>( options =>
				options.UseNpgsql( connectionString ) );

			return services;
		}

		///<summary>
		/// 게임 로직 서비스 등록
		/// </summary>
		private static IServiceCollection AddGameServices( this IServiceCollection services )
		{
			// Room 시스템
			services.AddSingleton<IRoomManager, RoomManager>();

			return services;
		}

		/// <summary>
		/// 데이터 관리 서비스 등록
		/// </summary>
		private static IServiceCollection AddDataServices( this IServiceCollection services )
		{
			// 기본 데이터 관리
			services.AddSingleton<DataManager>();

			return services;
		}

		/// <summary>
		/// 로깅 서비스 등록
		/// </summary>
		private static IServiceCollection AddLoggingServices( this IServiceCollection services, IConfiguration configuration )
		{
			services.AddLogging( loggingBuilder =>
			{
				loggingBuilder.ClearProviders();
				loggingBuilder.AddSerilog( dispose: true );
			} );

			Log.Logger = new LoggerConfiguration()
				.ReadFrom.Configuration( configuration )
				.CreateLogger();

			return services;
		}
	}
}
