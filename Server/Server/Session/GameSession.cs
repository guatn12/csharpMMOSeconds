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
			Send( 2, Encoding.UTF8.GetBytes( "Welcome to MMORPG Server" ) );
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			Console.WriteLine( $"[Disconnected] {endPoint}" );
		}

		public override void OnRecvPacket( ArraySegment<byte> buffer )
		{
			ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset );
			ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + 2);

			Console.WriteLine( $"[Recv Packet] Size : {size}, Id : {id}");

			string msg = Encoding.UTF8.GetString(buffer.Array, buffer.Offset + 4, size - 4);
			Console.WriteLine( $"[Recv] {msg}" );
			Send( 3, Encoding.UTF8.GetBytes( $"Echo: {msg}" ) );
		}

		public override void OnSend( int bytes )
		{
			Console.WriteLine( $"[Send] {bytes} bytes" );
		}
	}
}
