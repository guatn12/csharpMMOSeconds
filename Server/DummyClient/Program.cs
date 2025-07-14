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
			byte[] sendBuffer = Encoding.UTF8.GetBytes("Hello Server!!!");

			socket.Send( sendBuffer );
			int recvLen = socket.Receive(recvBuffer);
			string recvMsg = Encoding.UTF8.GetString(recvBuffer, 0, recvLen);
			Console.WriteLine( recvMsg );

			socket.Close();
		}
	}
}
