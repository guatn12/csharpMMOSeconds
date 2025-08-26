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
			int moveCount = 0;
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

					Thread.Sleep( 1000 );
				}
				catch( Exception ex )
				{
					_logger.LogError( ex, "MainLoop 중 오류 발생" );
					break;
				}
			}
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