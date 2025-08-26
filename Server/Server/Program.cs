using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Server.Configuration;
using Server.Configuration.Services;
using Server.Configuration.Validators;
using Server.Configuration.Security;
using ServerCore;
using Serilog;
using Microsoft.Extensions.Logging;
using Server.Data.Storage;
using Server.Data.HotReload;
using Server.Data;
using Server.Data.FileWatcher;
using Server.Room;
using Microsoft.Extensions.Hosting;

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
				IConfiguration configuration = BuildConfiguration(args);
				
				// 실제 로드된 설정값 확인
				Log.Information($"로드된 MaxQueueSize: {configuration["ServerConfiguration:JobQueue:MaxQueueSize"]}");

				// 민감정보 필수 검증
				ValidateSecrets( configuration );

				// DI 컨테이너 설정
				ServiceCollection services = new ServiceCollection();

				// 설정 바인딩 및 검증자 등록
				services.Configure<ServerConfiguration>( configuration.GetSection( "ServerConfiguration" ) );
				
				// 개별 섹션 바인딩 추가
				services.Configure<NetworkSettings>( configuration.GetSection( "ServerConfiguration:Network" ) );
				services.Configure<LoggingSettings>( configuration.GetSection( "ServerConfiguration:Logging" ) );
				services.Configure<SecuritySettings>( configuration.GetSection( "ServerConfiguration:Security" ) );
				services.Configure<DatabaseSettings>( configuration.GetSection( "ServerConfiguration:Database" ) );
				services.Configure<JobQueueSettings>( configuration.GetSection( "ServerConfiguration:JobQueue" ) );
				services.Configure<GameDataSettings>( configuration.GetSection( "ServerConfiguration:GameData" ) );
				services.Configure<RoomSettings>( configuration.GetSection( "ServerConfiguration:Room" ) );

				// 개별 설정 검증자 등록.
				services.AddSingleton<IValidateOptions<NetworkSettings>, NetworkSettingsValidator>();
				services.AddSingleton<IValidateOptions<LoggingSettings>, LoggingSettingsValidator>();
				services.AddSingleton<IValidateOptions<SecuritySettings>, SecuritySettingsValidator>();
				services.AddSingleton<IValidateOptions<DatabaseSettings>, DatabaseSettingsValidator>();
				services.AddSingleton<IValidateOptions<JobQueueSettings>, JobQueueSettingsValidator>();
				services.AddSingleton<IValidateOptions<RoomSettings>, RoomSettingsValidator>();

				// ConfigurationService 등록
				services.AddSingleton<IConfigurationService, ConfigurationService>();
				services.AddSingleton<Listener>();
				// GameSession을 팩토리 패턴으로 등록 (IRoomManager 의존성 주입 포함
				services.AddTransient<GameSession>(provider =>
				{
					var logger = provider.GetRequiredService<ILogger<GameSession>>();
					var roomManager = provider.GetRequiredService<IRoomManager>();
					return new GameSession( logger, roomManager );
				});
				services.AddLogging(loggingBuilder =>
				{
					loggingBuilder.ClearProviders();		// 기본 공급자 제거
					loggingBuilder.AddSerilog(dispose: true);
				} ); // ILogger<T> 의존성 추가.

				Log.Logger = new LoggerConfiguration()
					.ReadFrom.Configuration( configuration )
					.CreateLogger();

				// packet 핸들러 등록
				services.AddSingleton<IMovementPacketHandler, MovementPacketHandler>();
				services.AddSingleton<IChatPacketHandler, ChatPacketHandler>();
				services.AddSingleton<IPacketHandler, ServerPacketHandler>();

				// 데이터 관리 제공자 등록
				services.AddSingleton<IDataStorageProvider, DataStorageProvider>();
				services.AddSingleton<IHotReloadHandler, HotReloadHandler>();
				services.AddSingleton<IFileWatcher, GameDataFileWatcher>();
				services.AddSingleton<IDataManager, DataManager>();

				// room 관련 서비스 등록
				services.AddSingleton<IRoomFactory, RoomFactory>();
				services.AddSingleton<IRoomManager, RoomManager>();

				var serviceProvider = services.BuildServiceProvider();

				var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

				_listener = serviceProvider.GetRequiredService<Listener>();
				var jobQueueLogger = serviceProvider.GetRequiredService<ILogger<JobQueueManager>>();
				JobQueueManager.Initialize(jobQueueLogger);
				IRoomManager roomManager = serviceProvider.GetRequiredService<IRoomManager>();
				if(roomManager is IHostedService hostedRoomManager)
				{
					await hostedRoomManager.StartAsync( CancellationToken.None );
				}

				// 설정 검증 및 로드
				var configService = serviceProvider.GetRequiredService<IConfigurationService>();

				// ConfigurationService 초기화 (Hot Reload 활성화)
				await configService.InitializeAsync();

				// 게임 데이터 로딩 및 검증
				var dataManager = serviceProvider.GetRequiredService<IDataManager>();
				logger.LogInformation( "Starting game data loading..." );

				bool dataLoadSuccess = await dataManager.LoadAllDataAsync();
				if(!dataLoadSuccess)
				{
					logger.LogError( "Failed to load game data. Server startup aborted." );
					return;
				}

				bool dataValidateionSuccess = dataManager.ValidateAllData();
				if(!dataValidateionSuccess)
				{
					logger.LogError( "Game data validation failed. Server startup aborted." );
					return;
				}

				logger.LogInformation( "Game data loaded and validated successfully." );

				// json 데이터 파일 변경 확인 시작.
				var fileWatcher = serviceProvider.GetRequiredService<IFileWatcher>();
				fileWatcher.StartWatching();

				var hotReloadHandler = serviceProvider.GetService<IHotReloadHandler>();
				fileWatcher.FileChanged += async ( sender, args ) =>
				{
					logger.LogInformation( "Hot reload triggered: {TableName}", args.TableName );
					bool success = await hotReloadHandler.ReloadDataAsync(args.TableName, args.FilePath);
					logger.LogInformation( success ? "Hoit reload success: {TableName}" : "Hot reload failed: {TableName}", args.TableName );
				};

				// 설정 변경 이벤트 등록
				configService.ConfigurationChanged += ( sender, args ) =>
				{
					// 민감정보 마스킹 처리
					var maskedOldValue = SecurityHelper.IsSensitive(args.SectionName) 
						? SecurityHelper.MaskSensitiveValue(args.OldValue?.ToString() ?? "") 
						: args.OldValue?.ToString() ?? "";
					var maskedNewValue = SecurityHelper.IsSensitive(args.SectionName) 
						? SecurityHelper.MaskSensitiveValue(args.NewValue?.ToString() ?? "") 
						: args.NewValue?.ToString() ?? "";

					logger.LogInformation( "설정 변경됨: {SectionName}, Old: {OldValue}, New: {NewValue}", 
						args.SectionName, maskedOldValue, maskedNewValue );

					// 중요한 설정 변경 시 추가 처리
					if(args.SectionName == "NetworkSettings")
					{
						logger.LogWarning( "네트워크 설정이 변경되었습니다. 서버 재시작이 필요할 수 있습니다." );
					}
					else if(args.SectionName == "SecuritySettings")
					{
						logger.LogWarning( "보안 설정이 변경되었습니다." );
					}
				};

				var serverConfig = configService.Current;
				
				// 주소 검증
				IPAddress ipAddr;
				if(!IPAddress.TryParse( serverConfig.Network.Host, out ipAddr ))
				{
					logger.LogError( "Invalid Host IP Address in appsettings.json: {Host}", serverConfig.Network.Host );
					return;
				}

				IPEndPoint endPoint = new IPEndPoint(ipAddr, serverConfig.Network.Port);

				logger.LogInformation($"port:{serverConfig.Network.Port}");

				// job queue 시스템 초기화 및 worker 스레드 실행
				int threadCount = serverConfig.JobQueue.WorkerThreadCount > 0
					? serverConfig.JobQueue.WorkerThreadCount 
					: Environment.ProcessorCount;
				JobQueueManager.Instance.Start( threadCount );

				// 안전한 종료를 위한 이벤트 핸들러 등록
				Console.CancelKeyPress += ( sender, e ) =>
				{
					logger.LogInformation( "Stopping server... (Ctrl+C pressed)" );
					_shutdownEvent.Set();
					e.Cancel = true;    // 기본 종료 동작을 막습니다.
				};

				IPacketHandler handler = serviceProvider.GetRequiredService<IPacketHandler>();
				PacketManagerInstance = new PacketManager( handler );

				_listener.Init( endPoint, () => serviceProvider.GetRequiredService<GameSession>(), serverConfig.Network.ListenBacklog );

				logger.LogInformation( "Listening..." );

				_shutdownEvent.WaitOne();
			}
			catch ( Exception ex )
			{
				Log.Fatal( "Server start-up failed.", ex );
			}
			finally
			{
				await JobQueueManager.Instance.StopAsync();
				Log.CloseAndFlush();
			}
		}

		private static void ValidateSecrets(IConfiguration configuration)
		{
			var security = configuration.GetSection("ServerConfiguration:Security").Get<SecuritySettings>();
			var database = configuration.GetSection("ServerConfiguration:Database").Get<DatabaseSettings>();

			if(string.IsNullOrWhiteSpace( security?.EncryptionKey ))
				throw new InvalidOperationException( "EncryptionKey가 설정되지 않았습니다. User Secrets를 확인하세요" );

			if(string.IsNullOrWhiteSpace(security?.TokenSecret))
				throw new InvalidOperationException( "TokenSecret 설정되지 않았습니다. User Secrets를 확인하세요" );

			if(string.IsNullOrWhiteSpace(database?.ConnectionString))
				throw new InvalidOperationException( "Database ConnectionString 설정되지 않았습니다. User Secrets를 확인하세요" );

			Log.Information( "필수 민감정보 검증 완료" );
		}

		private static IConfiguration BuildConfiguration( string[] args )
		{
			var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
			Log.Information( $"현재 환경: {environmentName}" );

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
	}
}
