using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DummyClient
{
	internal class Program
	{
		static void Main( string[] args )
		{
			string host = Dns.GetHostName();
			IPHostEntry ipHost = Dns.GetHostEntry(host);
			IPAddress ipAddr = ipHost.AddressList[2];
			IPEndPoint endPoint = new IPEndPoint(ipAddr, 7777);

			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			socket.Connect( endPoint );

			byte[] recvBuffer = new byte[1024];
			byte[] sendBuffer = new byte[1024];
			string sendText = "Hello Server!!!";
			ushort packetSize = (ushort)Encoding.UTF8.GetByteCount(sendText);
			BitConverter.TryWriteBytes( new Span<byte>( sendBuffer, 0, sizeof(ushort) ), packetSize );
			Encoding.UTF8.GetBytes( sendText, 0, sendText.Length, sendBuffer, sizeof( ushort ) );
			ArraySegment<byte> sendData = new ArraySegment<byte>(sendBuffer, 0, packetSize + sizeof(ushort) );
			
			socket.Send( sendData );
			int recvLen = socket.Receive(recvBuffer);
			string recvMsg = Encoding.UTF8.GetString(recvBuffer, 0, recvLen);
			Console.WriteLine( recvMsg );

			socket.Close();
		}
	}
}
