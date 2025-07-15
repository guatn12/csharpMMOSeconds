using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
	public class GameSession : Session
	{
		public override void OnConnected( EndPoint endPoint )
		{
			Console.WriteLine( $"[Connected] {endPoint}" );
			Send( Encoding.UTF8.GetBytes( "Welcome to MMORPG Server" ) );
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			Console.WriteLine( $"[Disconnected] {endPoint}" );
		}

		public override void OnRecvPacket( ArraySegment<byte> buffer )
		{
			string msg = Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count);
			Console.WriteLine( $"[Recv] {msg}" );
			Send( Encoding.UTF8.GetBytes( $"Echo: {msg}" ) );
		}

		public override void OnSend( int bytes )
		{
			Console.WriteLine( $"[Send] {bytes} bytes" );
		}
	}
}
