using Microsoft.Extensions.Configuration;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DummyClient
{
	public class ConnectionSettings
	{
		public string Host { get; set; }
		public int Port { get; set; }
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

			ConnectionSettings settings = configuration.GetSection("ConnectionSettings").Get<ConnectionSettings>();

			if(settings == null)
			{
				Console.WriteLine( "Not find ConnectionSettings in appsettings.json" );
				return;
			}

			IPAddress ipAddr;
			if(!IPAddress.TryParse( settings.Host, out ipAddr ))
			{
				Console.WriteLine( "Invalid Host IP Address in appsettings.json" );
				return;
			}

			IPEndPoint endPoint = new IPEndPoint(ipAddr, settings.Port);

			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			socket.Connect( endPoint );
			Console.WriteLine($"Connected to {socket.RemoteEndPoint}");

			byte[] recvBuffer = new byte[1024];
			int recvLen = socket.Receive(recvBuffer);
			if(recvLen > 0)
			{
				HandleReceivedPacket(recvBuffer, recvLen);
			}

			while(true)
			{
				Console.Write( "> " );
				string message = Console.ReadLine();
				if(string.Equals(message, "q", StringComparison.OrdinalIgnoreCase))
				{
					break;
				}

				if(string.IsNullOrEmpty(message))
				{
					return;
				}

				byte[] messageBytes = Encoding.UTF8.GetBytes(message);
				ushort packetId = 1;
				ushort dataSize = (ushort)messageBytes.Length;
				ushort packetSize =  (ushort)(dataSize + 4);

				byte[] sendBuffer = new byte[packetSize];
				Buffer.BlockCopy( BitConverter.GetBytes( packetSize ), 0, sendBuffer, 0, sizeof( ushort ) );
				Buffer.BlockCopy( BitConverter.GetBytes( packetId ), 0, sendBuffer, 2, sizeof( ushort ) );
				Buffer.BlockCopy( messageBytes, 0, sendBuffer, 4, dataSize );

				socket.Send( sendBuffer );
				Console.WriteLine( $"[Send] Packet Size: {packetSize}, Id : {packetId}, Data: \"{message}\"" );

				recvLen = socket.Receive(recvBuffer);
				if(recvLen > 0)
				{
					HandleReceivedPacket( recvBuffer, recvLen );
				}
			}


			Console.WriteLine("Disconnected Server...");
			

			socket.Close();
		}

		static void HandleReceivedPacket( byte[] buffer, int length)
		{
			ushort recvPacketSize = BitConverter.ToUInt16(buffer, 0);
			ushort recvPacketId = BitConverter.ToUInt16(buffer, 2);

			if(length < recvPacketSize)
			{
				Console.WriteLine($"Error, Received data is smaller than packet size");
				return;
			}

			string recvMsg = Encoding.UTF8.GetString(buffer, 4, recvPacketSize - 4);
			Console.WriteLine($"[Recv] Id : {recvPacketId}, Size: {recvPacketSize}, Data : {recvMsg}");
		}
	}
}
