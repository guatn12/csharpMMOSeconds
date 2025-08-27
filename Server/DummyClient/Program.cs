using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using ServerCore;
using Protocol;
using Microsoft.Extensions.Logging;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using DummyClient.Configuration;
using Microsoft.Extensions.Options;
using DummyClient.Configuration.Services;
using System.Threading.Tasks;
using DummyClient.Packet;

namespace DummyClient
{
	public class ConnectionSettings
	{
		public string Host { get; set; }
		public int Port { get; set; }
	}

	internal class Program
	{
		public static ServerSession Session;
		public static PacketManager PacketManagerInstance { get; private set; }
		private static IServiceProvider _serviceProvider;
		private static ILogger<Program> _logger;

		static void Main( string[] args )
		{
			var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
			var builder = new ConfigurationBuilder()
				.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
				.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
				.AddEnvironmentVariables();

			IConfiguration configuration = builder.Build();

			// Serilog 설정
			Log.Logger = new LoggerConfiguration()
				.ReadFrom.Configuration(configuration)
				.CreateLogger();

			try
			{
				// DI 컨테이너 설정
				ServiceCollection services = new ServiceCollection();
				ConfigureServices( services, configuration );
				_serviceProvider = services.BuildServiceProvider();
				_logger = _serviceProvider.GetRequiredService<ILogger<Program>>();

				RunClient();
			}
			catch ( Exception ex )
			{
				Log.Fatal( ex, "DummyClient 실행 중 치명적 오류 발생" );
			}
			finally
			{
				Log.CloseAndFlush();
				//_serviceProvider?.Dispose();
			}
		}

		private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
		{
			// Configuration 등록
			services.Configure<ClientConfiguration>( configuration.GetSection( "ClientConfiguration" ) );

			// Validator 등록
			services.AddSingleton<IValidateOptions<ClientConfiguration>, ClientConfigurationValidator>();

			// Services 등록
			services.AddSingleton<IClientConfigurationService, ClientConfigurationService>();

			// Logger 등록
			services.AddLogging( builder =>
			{
				builder.ClearProviders();
				builder.AddSerilog();
			} );

			// New PacketManager and Handler Registration
			services.AddSingleton<BaseClientPacketHandler, ClientPacketHandler>();
			services.AddSingleton<PacketManager>();
		}

		private static void RunClient()
		{
			IClientConfigurationService configService = _serviceProvider.GetRequiredService<IClientConfigurationService>();
			ClientConfiguration config = configService.Current;

			// 설정 변경 감지 등록
			configService.RegisterChangeCallBack( newConfig =>
			{
				_logger.LogInformation( "클라이언트 설정이 변경되었습니다: {ServerHost}:{ServerPort}", newConfig.Connection.ServerHost,
					newConfig.Connection.ServerPort );
			} );

			IPEndPoint endPoint = new IPEndPoint(
				IPAddress.Parse(config.Connection.ServerHost),
				config.Connection.ServerPort);

			int clientCount = config.Simulation.ClientCount;

			if(1 < clientCount)
			{
				_logger.LogInformation( "다중 클라이언트 모드 시작 - 클라이언트 수: {ClientCount}", clientCount );
				RunMultipleClients( clientCount, endPoint, config );
			}
			else
			{
				_logger.LogInformation( "단일 클라이언트 모드 시작" );
				RunSingleClient( endPoint, config );
			}
		}

		private static void RunMultipleClients(int clientCount, IPEndPoint endPoint, ClientConfiguration config )
		{
			_logger.LogInformation( "=== 다중 클라이언트 테스트 시작 ===" );
			_logger.LogInformation( "서버: {EndPoint}, 클라이언트 수: {ClientCount}, 메시지 간격: {IntervalMs}ms",
				endPoint, clientCount, config.Simulation.MessageIntervalMs );

			List<Task> clientTasks = new List<Task>();
			CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

			// 각 클라이언트를 별도 Task로 실행
			for(int i = 0; i < clientCount; i++)
			{
				int clientId = i + 1;
				Task clientTask = Task.Run(() => RunClientInstance(clientId, endPoint, config, cancellationTokenSource.Token));
				clientTasks.Add( clientTask );

				// 연결 간격 (서버 부하 방지)
				Thread.Sleep( 200 );
			}

			_logger.LogInformation( "모든 클라이언트 시작 완료. 종료하라면 아무 키나 누르세요..." );

			// 콘솔 키 입력 대기
			Console.ReadKey();

			cancellationTokenSource.Cancel();

			bool allCompleted = Task.WaitAll(clientTasks.ToArray(), TimeSpan.FromSeconds(5));

			if(!allCompleted)
			{
				_logger.LogWarning( "일부 클라이언트가 정상 종료되지 않았습니다." );
			}

			_logger.LogInformation( "=== 다중 클라이언트 테스트 종료 ===" );
		}

		// 다중 클라이언트 실행
		private static void RunClientInstance( int clientId, IPEndPoint endPoint, ClientConfiguration config, CancellationToken cancellationToken)
		{
			ServerSession session = null;

			try
			{
				var logger = _serviceProvider.GetRequiredService<ILogger<Program>>();
				logger.LogInformation( "클라이언트 {ClientId} 시작", clientId );

				// PacketManager를 DI 컨테이너에서 직접 가져옴
				var packetManager = _serviceProvider.GetRequiredService<PacketManager>();

				Connector connector = new Connector();

				connector.Connect( endPoint, () =>
				{
					var sessionLogger = _serviceProvider.GetRequiredService<ILogger<ServerSession>>();
					// ServerSession 생성자에 DI에서 관리되는 PacketManager 인스턴스 주입
					session = new ServerSession( sessionLogger, packetManager );
					return session;
				} );

				// 연결 대기 (최대 5초)
				int waitCount = 0;
				while(session?.IsConnected() != true && waitCount < 50 && !cancellationToken.IsCancellationRequested)
				{
					Thread.Sleep( 100 );
					waitCount++;
				}

				if(cancellationToken.IsCancellationRequested)
				{
					logger.LogInformation( "클라이언트 {ClientId} 연결 취소됨.", clientId );
					return;
				}

				if(session?.IsConnected() != true)
				{
					logger.LogError( "클라이언트 {ClientId} 연결 실패 - 타임아웃", clientId );
					return;
				}

				logger.LogInformation( "클라이언트 {ClientId} 연결 성공", clientId );

				//개별 클라이언트 루프 실행
				RunClientLoop( clientId, session, config, logger, cancellationToken );
			}
			catch ( Exception ex )
			{
				var logger = _serviceProvider.GetService<ILogger<Program>>();
				logger.LogError( ex, "클라이언트 {ClientId} 실행 중 오류", clientId );
			}
		}

		// 개별 클라이언트 메인 루프
		private static void RunClientLoop(int clientId, ServerSession session, ClientConfiguration config,
			ILogger<Program> logger, CancellationToken cancellationToken)
		{
			int moveCount = 0;
			Random random = new Random(clientId * 1000); // 클라이언트별 다른 시도
			int messageInterval = config.Simulation.MessageIntervalMs;

			logger.LogInformation( "클라이언트 {ClientId} 루프 시작 - 메시지 간격: {IntervalMs}ms", clientId, messageInterval );

			while(session.IsConnected() && !cancellationToken.IsCancellationRequested )
			{
				try
				{
					// 이동 패킷 전송 (클라이언트별 고유 위치)
					C_Move movePacket = new C_Move()
					{
						PosInfo = new PosInfo()
						{
							PosX = clientId * 100 + moveCount,	// 클라이언트별 고유한 x 좌표 범위
							PosY = clientId * 10,				// 클라이언트별 고유한 y 좌표
							PosZ = moveCount % 50
						},
					};

					session.Send( movePacket );
					logger.LogDebug( "[Client {ClientId}] Send C_Move: pos=({X},{Y},{Z})", 
						clientId, movePacket.PosInfo.PosX, movePacket.PosInfo.PosY, movePacket.PosInfo.PosZ );

					// 채팅 패킷 전송 (5번마다 1번)
					if (moveCount % 5 == 0)
					{
						C_Chat chatPacket = new C_Chat()
						{
							Message = $"[Client-{clientId:D2}] 안녕하세요! {moveCount/5+1}번째 채팅 (총 이동: {moveCount})"
						};

						session.Send( chatPacket );
						logger.LogInformation( "[Client {ClientId}] Send C_Chat: {Message}", clientId, chatPacket.Message );
					}

					moveCount++;

					// 설정에서 가져온 메시지 간격 사용 (약간의 랜덤 요소 추가)
					int sleepTime = messageInterval + random.Next(-100, 100); // +- 100ms 랜덤
					sleepTime = Math.Max( sleepTime, 100 ); // 최소 100ms 보장

					// CancellationToken을 고려한 대기
					try
					{
						Task.Delay( sleepTime, cancellationToken ).Wait();
					}
					catch(OperationCanceledException)
					{
						break;
					}
				}
				catch (Exception ex)
				{
					logger.LogError( ex, "클라이언트 {ClientId} 루프 중 오류", clientId );
					break;
				}
			}
		}

		// 단일 클라이언트 실행 (기존 로직을 메서드로 분리)
		private static void RunSingleClient(IPEndPoint endPoint, ClientConfiguration config)
		{
			Connector connector = new Connector();
			_logger.LogInformation( "서버 연결 시도: {EndPoint}", endPoint );

			connector.Connect( endPoint, () =>
			{
				var sessionLogger = _serviceProvider.GetRequiredService<ILogger<ServerSession>>();
				var packetManager = _serviceProvider.GetRequiredService<PacketManager>();
				return new ServerSession( sessionLogger, packetManager );
			} );

			// 메인 루프는 기존과 동일...
			MainLoop(config);
		}

		private static void MainLoop(ClientConfiguration config)
		{
			int moveCount = 0;
			int messageInterval = config.Simulation.MessageIntervalMs;

			_logger.LogInformation( "단일 클라이언트 루프 시작 - 메시지 간격: {IntervalMs}ms", messageInterval );

			while(true)
			{
				try
				{
					if(Session == null)
					{
						Thread.Sleep( 300 );
						continue;
					}

					// 1초마다 이동
					C_Move movePacket = new C_Move()
					{
						PosInfo = new PosInfo() {PosX = moveCount++, PosY = 2, PosZ = 3 },
					};
					Session.Send( movePacket );
					_logger.LogInformation( $"[Send] C_Move: posX={movePacket.PosInfo.PosX}" );

					// 5초마다 채팅
					if(moveCount % 5 == 0)
					{
						C_Chat chatPacket = new C_Chat() {Message = $"안녕하세요! {moveCount / 5}번째 채팅입니다." };
						Session.Send( chatPacket );
						_logger.LogInformation( $"[Send] C_Chat: {chatPacket.Message}" );
					}

					Thread.Sleep( messageInterval );
				}
				catch( Exception ex )
				{
					_logger.LogError( ex, "MainLoop 중 오류 발생" );
					break;
				}
			}
		}
	}
}