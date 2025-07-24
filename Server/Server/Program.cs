using System;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Configuration;
using ServerCore;

namespace Server
{
	public class ServerSettings
	{
		public string Host { get; set; }
		public int Port { get; set; }
		public int ListenBacklog { get; set; }
		public string LogLevel { get; set; }
	}

	internal class Program
	{
		static Listener _listener = new Listener();
		public static PacketManager PacketManagerInstance { get; private set; }

		static ManualResetEvent _shutdownEvent = new ManualResetEvent(false);
		static void Main( string[] args )
		{
			// 로거 초기화
			LogManager.Init();
			try
			{
				// 환경 변수 가져오기
				var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

				var builder = new ConfigurationBuilder()
				.SetBasePath(AppDomain.CurrentDomain.BaseDirectory) // 실행 파일 기준 경로 설정
                .AddJsonFile("appsettings.json", optional:false, reloadOnChange: true)
				.AddJsonFile($"appsettings.{environmentName}.json", optional:true, reloadOnChange: true) // 현재 환경에 맞는 appsettings.json 로드(덮어쓰기)
				.AddEnvironmentVariables();

				IConfiguration configuration = builder.Build();

				ServerSettings settings = configuration.GetSection("ServerSettings").Get<ServerSettings>();

				if(settings == null)
				{
					LogManager.Error( "Not find ServerSettings in appsettings.json" );
					return;
				}

				IPAddress ipAddr;
				if(!IPAddress.TryParse( settings.Host, out ipAddr ))
				{
					LogManager.Error( null, "Invalid Host IP Address in appsettings.json: {Host}", settings.Host );
					return;
				}

				IPEndPoint endPoint = new IPEndPoint(ipAddr, settings.Port);

				// job queue 시스템 초기화 및 worker 스레드 실행
				int threadCount = Environment.ProcessorCount;
				JobQueueManager.Instance.Start( threadCount );

				// 안전한 종료를 위한 이벤트 핸들러 등록
				Console.CancelKeyPress += ( sender, e ) =>
				{
					LogManager.Info( "Stopping server... (Ctrl+C pressed)" );
					//JobQueueManager.Instance.Stop();
					_shutdownEvent.Set();
					e.Cancel = true;    // 기본 종료 동작을 막습니다.
				};

				IPacketHandler handler = new ServerPacketHandler();
				PacketManagerInstance = new PacketManager( handler );

				_listener.Init( endPoint, () => new GameSession(), settings.ListenBacklog );

				LogManager.Info( "Listening..." );

				_shutdownEvent.WaitOne();
			}
			catch ( Exception ex )
			{
				LogManager.Fatal( "Server start-up failed.", ex );
			}
			finally
			{
				JobQueueManager.Instance.Stop();
				LogManager.CloseAndFlush();
			}
		}
	}
}
