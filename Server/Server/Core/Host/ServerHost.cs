using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Server.Config;
using Server.Core.Session;
using Server.Data;
using Server.Infra;
using Server.Packet;
using Server.Room;
using ServerCore;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Core.Host
{
	public class ServerHost : IHostedService
	{
		private readonly ILogger<ServerHost> _logger;
		private readonly IOptions<ServerSettings> _serverSettings;
		private readonly IJobQueueManager _jobQueueManager;
		private readonly DataManager _dataManager;
		private readonly RedisService _redisService;
		private readonly IRoomManager _roomManager;
		private readonly SystemHealthService _healthService;
		private readonly PerformanceMonitoringService _performanceMonitoringService;
		private readonly PacketManager _packetManager;
		private readonly Listener _listener;
		private readonly IServiceProvider _serviceProvider;
		private readonly ISessionManager _sessionManager;
		private CancellationTokenSource _cancellationTokenSource;

		public ServerHost(
			ILogger<ServerHost> logger,
			IOptions<ServerSettings> serverSettings,
			IJobQueueManager jobQueueManager,
			DataManager dataManager,
			RedisService redisService,
			IRoomManager roomManager,
			SystemHealthService systemHealthService,
			PerformanceMonitoringService performanceMonitoringService,
			PacketManager packetManager,
			Listener listener,
			IServiceProvider serviceProvider,
			ISessionManager sessionManager )
		{
			_logger = logger;
			_serverSettings = serverSettings;
			_jobQueueManager = jobQueueManager;
			_dataManager = dataManager;
			_redisService = redisService;
			_roomManager = roomManager;
			_healthService = systemHealthService;
			_performanceMonitoringService = performanceMonitoringService;
			_packetManager = packetManager;
			_listener = listener;
			_serviceProvider = serviceProvider;

			_cancellationTokenSource = new CancellationTokenSource();
			_sessionManager = sessionManager;
		}

		public async Task StartAsync(CancellationToken token)
		{
			try
			{
				// Redis 연결 테스트 추가
				await TestRedisConnectionAsync();

				// 게임 데이터 로딩
				await LoadGameDataAsync();

				// 핵심 서비스 초기화
				await InitializeCoreServicesAsync();

				// 서버 시작
				StartNetworkServerAsync();

				// 종료 대기
				_logger.LogInformation( "서버가 시작되었습니다. Ctrl+C로 종료하세요." );
				_logger.LogInformation( "Listening..." );
			}
			catch(Exception ex)
			{
				Log.Fatal( ex, "서버 시작 실패" );
				throw; // 예외를 다시 던져서 서버가 중단되도록 함
			}
		}

		public async Task StopAsync(CancellationToken token)
		{
			_logger.LogInformation( "서버 종료 중..." );

			// 취소 신호 발송
			_cancellationTokenSource.Cancel();

			// 리스너 정지
			//_listener.Stop();

			// JobQueue 정지
			await _jobQueueManager.StopAsync();

			// RoomManager 정지
			if(_roomManager is IHostedService hostedRoomManager)
			{
				await hostedRoomManager.StopAsync( token );
			}

			_logger.LogInformation( "서버 종료 완료" );
		}

		public async Task TestRedisConnectionAsync()
		{
			try
			{
				_logger.LogInformation( "Redis 연결 테스트를 시작합니다..." );

				bool isConnected = await _redisService.PingAsync();
				if(isConnected)
				{
					_logger.LogInformation( "Redis 연결 성공!" );

					// 기본 CRUD 테스트
					await _redisService.SetAsync( "server:startup", DateTime.Now.ToString(), TimeSpan.FromMinutes( 5 ) );
					var startupTime = await _redisService.GetStringAsync("server:startup");
					_logger.LogInformation( "Redis 테스트 데이터 저장/조회 성공: {StartupTime}", startupTime );
				}
				else
				{
					_logger.LogError( "Redis 연결 실패! 서버를 계속 실행하지만 Redis 기능은 사용할 수 없습니다." );
				}
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Redis 연결 테스트 중 오류 발생" );
			}
		}

		private async Task InitializeCoreServicesAsync()
		{
			ServerSettings settings = _serverSettings.Value;

			// JobQueueManager 초기화
			//var jobQueueLogger = _serviceProvider.GetRequiredService<ILogger<JobQueueManager>>();
			//JobQueueManager.Initialize( jobQueueLogger );

			// JobQueue 시작
			int threadCount = 0 < settings.JobQueue.WorkerThreadCount
				? settings.JobQueue.WorkerThreadCount
				: Environment.ProcessorCount;
			_jobQueueManager.Start( threadCount );
			//JobQueueManager.Instance.Start( threadCount );

			// RoomManager 시작
			if (_roomManager is IHostedService hostedRoomManager)
				await hostedRoomManager.StartAsync(CancellationToken.None);

			// 시스템 상태 체크 및 모니터링 시작
			await InitializeMonitoringServicesAsync();

			_logger.LogInformation( "핵심 서비스 초기화 완료" );
		}

		private async Task InitializeMonitoringServicesAsync()
		{
			try
			{
				// 초기 시스템 상태 체크
				bool initialHealth = await _healthService.CheckSystemHealthAsync();

				if(!initialHealth)
				{
					_logger.LogWarning( "Initiali system health check failed, but continuing startup..." );
				}

				// 성능 모니터링 서비스 시작
				_logger.LogInformation( "Performance monitoring started" );

				// 주기적 Health Check 시작 (60초)
				_ = Task.Run( async () => await StartPeriodicHealthCheckAsync() );

				_logger.LogInformation( "Monitoring services initialized successfully" );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to initalize monitoring services" );
			}
		}

		private async Task StartPeriodicHealthCheckAsync()
		{
			while(!_cancellationTokenSource.Token.IsCancellationRequested) 
			{
				try
				{
					await Task.Delay( 60000 );
					await _healthService.CheckSystemHealthAsync();
				}
				catch(Exception ex)
				{
					_logger.LogError( ex, "Periodic health check failed" );
				}
			}
		}

		private async Task LoadGameDataAsync()
		{
			_logger.LogInformation( "게임 데이터 로딩 시작..." );

			bool loadSuccess = await _dataManager.LoadAllDataAsync();
			if(!loadSuccess)
				throw new InvalidOperationException( "게임 데이터 로딩 실패" );

			bool validateSuccess = _dataManager.ValidateAllData();
			if(!validateSuccess)
				throw new InvalidOperationException( "게임 데이터 검증 실패" );

			_logger.LogInformation( "게임 데이터 로딩 및 검증 완료" );
		}

		private void StartNetworkServerAsync()
		{
			ServerSettings settings = _serverSettings.Value;
			// 네트워크 설정
			if(!IPAddress.TryParse( settings.Network.Host, out IPAddress ipAddr ))
				throw new InvalidOperationException( $"잘못된 IP 주소: {settings.Network.Host}" );

			IPEndPoint endPoint = new IPEndPoint(ipAddr, settings.Network.Port);

			// 종료 이벤트 핸들러
			Console.CancelKeyPress += ( sender, e ) =>
			{
				_logger.LogInformation( "서버 종료 중 ... (Ctrl+C 감지)" );
				e.Cancel = true;
			};

			// 리스너 시작
			_listener.Init( endPoint, () => _sessionManager.CreateSession(), settings.Network.ListenBacklog );
			_logger.LogInformation( "서버 리스닝 시작: {EndPoint}", endPoint );
		}
	}
}
