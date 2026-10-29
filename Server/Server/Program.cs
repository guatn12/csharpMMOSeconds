using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Microsoft.Extensions.Hosting;
using Server.Extensions;
using Server.Core.Host;

namespace Server
{
	internal class Program
	{
		static async Task Main( string[] args )
		{
			try
			{
				// Generic Host 빌더 사용
				var host = Host.CreateDefaultBuilder(args)
					.ConfigureAppConfiguration((context, config) =>
					{
						// User Secrets, 환경별 설정 등
					})
					.ConfigureServices((context, services) =>
					{
						// 기존 services.AddAppServices() 호출
						services.AddAppServices(context.Configuration, context.HostingEnvironment);
						services.AddHostedService<ServerHost>();	// 핵심.
					})
					.UseSerilog((context, services, configuration) => configuration
						.ReadFrom.Configuration(context.Configuration)
						.Enrich.FromLogContext())
					.Build();

				await host.RunAsync();
			}
			catch ( Exception ex )
			{
				Log.Fatal( ex, "서버 시작 실패" );
			}
			finally
			{
				//await JobQueueManager.Instance.StopAsync();
				Log.CloseAndFlush();
			}
		}
	}
}
