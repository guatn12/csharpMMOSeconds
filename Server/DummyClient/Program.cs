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

			// Packet Handlers 등록
			services.AddSingleton<IMovementPacketHandler, MovementPacketHandler>();
			services.AddSingleton<IChatPacketHandler, ChatPacketHandler>();
			services.AddSingleton<ISystemPacketHandler, SystemPacketHandler>();
			services.AddSingleton<IGamePlayPacketHandler, GamePlayPacketHandler>();

			// ClientPacketHandler도 DI에서 관리
			services.AddSingleton<IPacketHandler, ClientPacketHandler>();
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

			Connector connector = new Connector();
			IPacketHandler packetHandler = _serviceProvider.GetRequiredService<IPacketHandler>();
			PacketManagerInstance = new PacketManager( packetHandler );

			_logger.LogInformation( "서버 연결 시도: {EndPoint}", endPoint );
			connector.Connect( endPoint, () => new ServerSession( _serviceProvider.GetRequiredService<ILogger<ServerSession>>() ) );

			// 메인 루프는 기존과 동일...
			MainLoop();
		}

		private static void MainLoop()
		{
			while(true)
			{
				if(Session == null)
				{
					Thread.Sleep( 100 );
					continue;
				}

				Console.WriteLine( "\n------------------------------------" );
				Console.WriteLine( "전송할 패킷을 선택하세요:" );
				Console.WriteLine( "1. 이동 (C_Move)" );
				Console.WriteLine( "2. 채팅 (C_Chat)" );
				Console.WriteLine( "3. ALL 랜덤 테스트" );
				Console.WriteLine( "Q. 종료" );
				Console.Write( "> " );

				string input = Console.ReadLine();
				if(string.Equals( input, "q", StringComparison.OrdinalIgnoreCase ))
				{
					break;
				}

				switch(input)
				{
				case "1":
					RunTest( TestScenario.C_MOVE );
					break;

				case "2":
					RunTest( TestScenario.C_CHAT );
					break;

				case "3":
					Random rand = new Random();
					_logger.LogInformation( "\n'전체 테스트'를 랜덤하게 반복합니다. 중지하려면 아무 키나 누르세요..." );
					while(!Console.KeyAvailable)
					{
						// 0 또는 1을 랜덤하게 생성하여 테스트 선택
						if(rand.Next( 0, 2 ) == 0)
						{
							RunTest( TestScenario.C_MOVE );
						}
						else
						{
							RunTest( TestScenario.RANDOMCHAT );
						}
						Thread.Sleep( 100 ); // 0.1초 간격
					}
					// 입력 버퍼 비우기
					while(Console.KeyAvailable) Console.ReadKey( true );
					_logger.LogInformation( "반복 테스트를 중지했습니다." );
					break;
				default:
					_logger.LogWarning( "잘못된 입력입니다." );
					break;
				}
				Thread.Sleep( 200 );
			}
			_logger.LogInformation( "클라이언트를 종료합니다." );
		}
		

		enum TestScenario
		{
			C_MOVE,
			C_CHAT,
			RANDOMCHAT,
		}

		static void RunTest( TestScenario scenario )
		{
			switch(scenario)
			{
			case TestScenario.C_MOVE:
				C_Move movePacket = new C_Move()
				{
					PosInfo = new PosInfo() {PosX = 1, PosY = 2, PosZ = 3},
				};
				Session.Send( movePacket );
				_logger.LogInformation( "[Send] C_Move" );
				break;

			case TestScenario.C_CHAT:
				_logger.LogInformation( "채팅 메시지 입력: " );
				string message = Console.ReadLine();
				if(string.IsNullOrEmpty( message ))
				{
					_logger.LogWarning( "메시지가 비어있습니다." );
					return;
				}

				C_Chat chat = new C_Chat() { Message = message };
				Session.Send( chat );
				_logger.LogInformation( $"[Send] C_Chat: {message}" );
				break;
			case TestScenario.RANDOMCHAT:
				Random rand = new Random();

				string randomchat = rand.NextInt64().ToString();
				C_Chat ranChat = new C_Chat() {Message = randomchat};
				Session.Send( ranChat );
				_logger.LogInformation($"[Send] C_Chat: {ranChat}");
				break;
			}
		}
	}
}