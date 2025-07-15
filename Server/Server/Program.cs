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
		static void Main( string[] args )
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
				Console.WriteLine( "Not find ServerSettings in appsettings.json" );
				return;
			}

			IPAddress ipAddr;
			if (!IPAddress.TryParse(settings.Host, out ipAddr))
			{
				Console.WriteLine( "Invalid Host IP Address in appsettings.json" );
				return;
			}

			IPEndPoint endPoint = new IPEndPoint(ipAddr, settings.Port);

			Listerner listerner = new Listerner();
			listerner.Init( endPoint, () => new GameSession(), settings.ListenBacklog );

			
			Console.WriteLine( "Linstening..." );

			while(true)
			{
				Thread.Sleep( 1000 );
			}
		}
	}
}
