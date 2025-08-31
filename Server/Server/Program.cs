using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Server.Packet;
using ServerCore;
using Serilog;
using Microsoft.Extensions.Logging;
using Server.Data;
using Server.Room;
using Microsoft.Extensions.Hosting;
using Server.Extensions;
using Server.Core.Session;
using Server.Config;
using Server.Infra;

namespace Server
{
	internal class Program
	{
		static Listener _listener;
		public static PacketManager PacketManagerInstance { get; private set; }
		static ManualResetEvent _shutdownEvent = new ManualResetEvent(false);

		static async Task Main( string[] args )
		{
			// 로거 초기화
			Log.Logger = new LoggerConfiguration()
				.WriteTo.Console()
				.CreateLogger();

			try
			{
				// 설정 빌드
				IConfiguration configuration = BuildConfiguration(args);

				// DI 컨테이너 설정(단일 확장 메서드로 통합)
				ServiceCollection services = new ServiceCollection();
				services.AddAppServices(configuration);

				var serviceProvider = services.BuildServiceProvider();
				var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

				// Redis 연결 테스트 추가
				await TestRedisConnectionAsync( serviceProvider, logger );

				// 핵심 서비스 초기화
				await InitializeCoreServicesAsync(serviceProvider, logger);

				// 게임 데이터 로딩
				await LoadGameDataAsync( serviceProvider, logger );

				// 서버 시작
				await StartServerAsync( serviceProvider, logger );

				// 종료 대기
				logger.LogInformation( "서버가 시작되었습니다. Ctrl+C로 종료하세요." );
				logger.LogInformation( "Listening..." );

				_shutdownEvent.WaitOne();
			}
			catch ( Exception ex )
			{
				Log.Fatal( ex, "서버 시작 실패" );
			}
			finally
			{
				await JobQueueManager.Instance.StopAsync();
				Log.CloseAndFlush();
			}
		}

		private static async Task InitializeCoreServicesAsync(ServiceProvider serviceProvider, ILogger<Program> logger)
		{
			// JobQueueManager 초기화
			var jobQueueLogger = serviceProvider.GetRequiredService<ILogger<JobQueueManager>>();
			JobQueueManager.Initialize( jobQueueLogger );

			// RoomManager 시작
			var roomManager = serviceProvider.GetRequiredService<IRoomManager>();
			if(roomManager is IHostedService hostedRoomManager)
			{
				await hostedRoomManager.StartAsync( CancellationToken.None );
			}

			logger.LogInformation( "핵심 서비스 초기화 완료" );
		}

		private static async Task LoadGameDataAsync(ServiceProvider serviceProvider, ILogger<Program> logger)
		{
			var dataManager = serviceProvider.GetRequiredService<DataManager>();

			logger.LogInformation( "게임 데이터 로딩 시작..." );

			bool loadSuccess = await dataManager.LoadAllDataAsync();
			if(!loadSuccess)
				throw new InvalidOperationException( "게임 데이터 로딩 실패" );

			bool validateSuccess = dataManager.ValidateAllData();
			if(!validateSuccess)
				throw new InvalidOperationException( "게임 데이터 검증 실패" );

			logger.LogInformation( "게임 데이터 로딩 및 검증 완료" );
		}

		private static async Task StartServerAsync(ServiceProvider serviceProvider, ILogger<Program> logger)
		{
			ServerSettings settings = serviceProvider.GetRequiredService<IOptions<ServerSettings>>().Value;

			// 네트워크 설정
			if(!IPAddress.TryParse(settings.Network.Host, out IPAddress ipAddr))
				throw new InvalidOperationException($"잘못된 IP 주소: {settings.Network.Host}");

			IPEndPoint endPoint = new IPEndPoint(ipAddr, settings.Network.Port);

			// JobQueue 시작
			int threadCount = 0 < settings.JobQueue.WorkerThreadCount
				? settings.JobQueue.WorkerThreadCount 
				: Environment.ProcessorCount;
			JobQueueManager.Instance.Start( threadCount );

			// 종료 이벤트 핸들러
			Console.CancelKeyPress += ( sender, e ) =>
			{
				logger.LogInformation( "서버 종료 중 ... (Ctrl+C 감지)" );
				_shutdownEvent.Set();
				e.Cancel = true;
			};

			// 리스너 시작
			_listener = serviceProvider.GetRequiredService<Listener>();
			PacketManagerInstance = serviceProvider.GetRequiredService<PacketManager>();

			_listener.Init( endPoint, () => serviceProvider.GetRequiredService<GameSession>(), settings.Network.ListenBacklog );
			logger.LogInformation( "서버 리스닝 시작: {EndPoint}", endPoint );
		}

		private static IConfiguration BuildConfiguration( string[] args )
		{
			var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
			var basePath = AppDomain.CurrentDomain.BaseDirectory;
			Log.Information( $"기본 경로: {basePath}" );
			Log.Information( $"로드할 환경 파일: appsettings.{environmentName}.json" );

			var builder = new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
				.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);

			// 개발 환경에서만 User Secrets 적용
			if(environmentName.Equals( "Development", StringComparison.OrdinalIgnoreCase ))
			{
				builder.AddUserSecrets<Program>();
			}

			// 환경변수가 최우선 (운영 환경에서 사용)
			builder.AddEnvironmentVariables();

			// 명령줄 인수가 최고 우선 순위
			if(0 < args?.Length)
			{
				builder.AddCommandLine( args );
			}

			return builder.Build();
		}

		private static async Task TestRedisConnectionAsync(IServiceProvider serviceProvider, ILogger<Program> logger)
		{
			try
			{
				RedisService redisService = serviceProvider.GetRequiredService<RedisService>();
				logger.LogInformation( "Redis 연결 테스트를 시작합니다..." );

				bool isConnected = await redisService.PingAsync();
				if(isConnected)
				{
					logger.LogInformation( "Redis 연결 성공!" );

					// 기본 CRUD 테스트
					await redisService.SetAsync( "server:startup", DateTime.Now.ToString(), TimeSpan.FromMinutes( 5 ) );
					var startupTime = await redisService.GetStringAsync("server:startup");
					logger.LogInformation( "Redis 테스트 데이터 저장/조회 성공: {StartupTime}", startupTime );
				}
				else
				{
					logger.LogError( "Redis 연결 실패! 서버를 계속 실행하지만 Redis 기능은 사용할 수 없습니다." );
				}
			}
			catch ( Exception ex )
			{
				logger.LogError( ex, "Redis 연결 테스트 중 오류 발생" );
			}
		}
	}
}
