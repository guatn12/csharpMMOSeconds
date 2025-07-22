using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using ServerCore;
using Protocol;

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

			IPacketHandler packetHandler = new ClientPacketHandler();
			PacketManagerInstance = new PacketManager( packetHandler );

			connector.Connect( endPoint, () => new ServerSession() );

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
					Console.WriteLine( "\n'전체 테스트'를 랜덤하게 반복합니다. 중지하려면 아무 키나 누르세요..." );
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
				Console.WriteLine( "[Send] C_Move" );
				break;

			case TestScenario.C_CHAT:
				Console.WriteLine( "채팅 메시지 입력: " );
				string message = Console.ReadLine();
				if(string.IsNullOrEmpty( message ))
				{
					Console.WriteLine( "메시지가 비어있습니다." );
					return;
				}

				C_Chat chat = new C_Chat() { Message = message };
				Session.Send( chat );
				Console.WriteLine( $"[Send] C_Chat: {message}" );
				break;
			case TestScenario.RANDOMCHAT:
				Random rand = new Random();

				string randomchat = rand.NextInt64().ToString();
				C_Chat ranChat = new C_Chat() {Message = randomchat};
				Session.Send( ranChat );
				Console.WriteLine($"[Send] C_Chat: {ranChat}");
				break;
			}
		}
	}
}