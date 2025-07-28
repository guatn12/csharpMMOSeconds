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

namespace Server
{
	internal class Program
	{
		static Listener _listener = new Listener();
		public static PacketManager PacketManagerInstance { get; private set; }

		static ManualResetEvent _shutdownEvent = new ManualResetEvent(false);
		static async Task Main( string[] args )
		{
			// 로거 초기화
			LogManager.Init();
			try
			{
				// 환경 변수 가져오기
				var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
				LogManager.Info($"현재 환경: {environmentName}");

				// 보안 환경변수 설정 (개발용 기본값 포함)
				Environment.SetEnvironmentVariable("ENCRYPTION_KEY", 
					SecurityHelper.GetSecureValue("ENCRYPTION_KEY", "dev-encryption-key-change-in-production"));
				Environment.SetEnvironmentVariable("TOKEN_SECRET", 
					SecurityHelper.GetSecureValue("TOKEN_SECRET", "dev-token-secret-change-in-production"));
				Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING", 
					SecurityHelper.GetSecureValue("DATABASE_CONNECTION_STRING", ""));

				var basePath = AppDomain.CurrentDomain.BaseDirectory;
				LogManager.Info($"기본 경로: {basePath}");
				LogManager.Info($"로드할 환경 파일: appsettings.{environmentName}.json");

				var builder = new ConfigurationBuilder()
				.SetBasePath(basePath) // 실행 파일 기준 경로 설정
                .AddJsonFile("appsettings.json", optional:false, reloadOnChange: true)
				.AddJsonFile($"appsettings.{environmentName}.json", optional:true, reloadOnChange: true) // 현재 환경에 맞는 appsettings.json 로드(덮어쓰기)
				.AddEnvironmentVariables();

				IConfiguration configuration = builder.Build();
				
				// 실제 로드된 설정값 확인
				LogManager.Info($"로드된 MaxQueueSize: {configuration["ServerConfiguration:JobQueue:MaxQueueSize"]}");

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
				
				services.AddSingleton<IValidateOptions<NetworkSettings>, NetworkSettingsValidator>();
				services.AddSingleton<IValidateOptions<LoggingSettings>, LoggingSettingsValidator>();
				services.AddSingleton<IValidateOptions<SecuritySettings>, SecuritySettingsValidator>();
				services.AddSingleton<IValidateOptions<DatabaseSettings>, DatabaseSettingsValidator>();
				services.AddSingleton<IValidateOptions<JobQueueSettings>, JobQueueSettingsValidator>();

				// ConfigurationService 등록
				services.AddSingleton<IConfigurationService, ConfigurationService>();
				services.AddLogging(); // ILogger<T> 의존성 추가.

				var serviceProvider = services.BuildServiceProvider();

				// 설정 검증 및 로드
				var configService = serviceProvider.GetRequiredService<IConfigurationService>();

				// ConfigurationService 초기화 (Hot Reload 활성화)
				await configService.InitializeAsync();

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

					LogManager.Info( "설정 변경됨: {SectionName}, Old: {OldValue}, New: {NewValue}", 
						args.SectionName, maskedOldValue, maskedNewValue );

					// 중요한 설정 변경 시 추가 처리
					if(args.SectionName == "NetworkSettings")
					{
						LogManager.Warning( "네트워크 설정이 변경되었습니다. 서버 재시작이 필요할 수 있습니다." );
					}
					else if(args.SectionName == "SecuritySettings")
					{
						LogManager.Warning( "보안 설정이 변경되었습니다." );
					}
				};

				var serverConfig = configService.Current;
				
				// 주소 검증
				IPAddress ipAddr;
				if(!IPAddress.TryParse( serverConfig.Network.Host, out ipAddr ))
				{
					LogManager.Error( null, "Invalid Host IP Address in appsettings.json: {Host}", serverConfig.Network.Host );
					return;
				}

				IPEndPoint endPoint = new IPEndPoint(ipAddr, serverConfig.Network.Port);

				LogManager.Info($"port:{serverConfig.Network.Port}");

				// job queue 시스템 초기화 및 worker 스레드 실행
				int threadCount = serverConfig.JobQueue.WorkerThreadCount > 0
					? serverConfig.JobQueue.WorkerThreadCount 
					: Environment.ProcessorCount;
				JobQueueManager.Instance.Start( threadCount );

				// 안전한 종료를 위한 이벤트 핸들러 등록
				Console.CancelKeyPress += ( sender, e ) =>
				{
					LogManager.Info( "Stopping server... (Ctrl+C pressed)" );
					_shutdownEvent.Set();
					e.Cancel = true;    // 기본 종료 동작을 막습니다.
				};

				IPacketHandler handler = new ServerPacketHandler();
				PacketManagerInstance = new PacketManager( handler );

				_listener.Init( endPoint, () => new GameSession(), serverConfig.Network.ListenBacklog );

				LogManager.Info( "Listening..." );

				_shutdownEvent.WaitOne();
			}
			catch ( Exception ex )
			{
				LogManager.Fatal( "Server start-up failed.", ex );
			}
			finally
			{
				await JobQueueManager.Instance.StopAsync();
				LogManager.CloseAndFlush();
			}
		}
	}
}
