using System;
using System.Net;
using System.Threading;
using ServerCore;

namespace Server
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string host = Dns.GetHostName();
            IPHostEntry ipHost = Dns.GetHostEntry(host);
            IPAddress ipAddr = ipHost.AddressList[2];
            IPEndPoint endPoint = new IPEndPoint(ipAddr, 7777);

            Listerner listerner = new Listerner();
            listerner.Init( endPoint, () => new GameSession( ) );
			Console.WriteLine("Linstening...");

            while(true)
            {
                Thread.Sleep( 1000 );
            }
        }
    }
}
