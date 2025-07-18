using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using ServerCore;

namespace DummyClient
{
	public class ConnectionSettings
	{
		public string Host { get; set; }
		public int Port { get; set; }
	}

	internal class Program
	{
		public static ServerSesssion Session;

		static void Main( string[] args )
		{
			var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
			var builder = new ConfigurationBuilder()
				.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
				.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
				.AddEnvironmentVariables();

			IConfiguration configuration = builder.Build();
			ConnectionSettings settings = configuration.GetSection("ConnectionSettings").Get<ConnectionSettings>();

			IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse(settings.Host), settings.Port);
			Connector connector = new Connector();

			connector.Connect( endPoint, () => new ServerSesssion() );

			while(true)
			{
				if(Session == null)
				{
					Thread.Sleep( 100 );
					continue;
				}

				Console.WriteLine( "\n------------------------------------" );
				Console.WriteLine( "테스트를 선택하세요:" );
				Console.WriteLine( "1. 작은 패킷 5개 동시 전송 (1회)" );
				Console.WriteLine( "2. 큰 패킷 1개 전송 (1회)" );
				Console.WriteLine( "3. [반복] 전체 테스트 랜덤 실행 (중지: 아무 키)" );
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
					RunTest( settings, TestScenario.SmallPackets );
					break;

				case "2":
					RunTest( settings, TestScenario.LargePacket );
					break;

				case "3":
					Random rand = new Random();
					Console.WriteLine( "\n'전체 테스트'를 랜덤하게 반복합니다. 중지하려면 아무 키나 누르세요..." );
					while(!Console.KeyAvailable)
					{
						// 0 또는 1을 랜덤하게 생성하여 테스트 선택
						if(rand.Next( 0, 2 ) == 0)
						{
							RunTest( settings, TestScenario.SmallPackets );
						}
						else
						{
							RunTest( settings, TestScenario.LargePacket );
						}
						Thread.Sleep( 100 ); // 0.1초 간격
					}
					// 입력 버퍼 비우기
					while(Console.KeyAvailable) Console.ReadKey( true );
					Console.WriteLine( "반복 테스트를 중지했습니다." );
					break;

				default:
					Console.WriteLine( "잘못된 입력입니다." );
					break;
				}
				Thread.Sleep( 200 );
			}
			Console.WriteLine( "클라이언트를 종료합니다." );
		}

		enum TestScenario
		{
			SmallPackets,
			LargePacket
		}

		static void RunTest( ConnectionSettings settings, TestScenario scenario )
		{
			switch(scenario)
			{
			case TestScenario.SmallPackets:
				Console.WriteLine( "[테스트 1] 5개의 작은 패킷을 연속으로 전송합니다..." );
				for (int i = 0; i < 5; i++)
				{
					string message = $"Test{i}";
					byte[] data = Encoding.UTF8.GetBytes( message );
					Session.Send( (ushort)i, data );
				}
				
				Console.WriteLine( $" -> 5개 패킷 전송 완료." );
				break;

			case TestScenario.LargePacket:
				Console.WriteLine( "[테스트 2] 1개의 큰 패킷(33바이트)을 전송합니다..." );
				string largeMessage = "This is a very big packet data!";
				byte[] largePacket = Encoding.UTF8.GetBytes( largeMessage );
				Session.Send( 100, largePacket );
				Console.WriteLine( $" -> {largePacket.Length + 4} 바이트 전송 완료." );
				break;
			}
		}
	}
}