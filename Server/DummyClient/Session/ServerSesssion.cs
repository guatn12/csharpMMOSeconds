using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DummyClient
{
	public class ServerSesssion : Session
	{
		public override void OnConnected( EndPoint endPoint )
		{
			Console.WriteLine( $"OnConnected: {endPoint}" );
			Program.Session = this;
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			Console.WriteLine( $"OnDisConnected: {endPoint}" );
			Program.Session = null;
		}

		public override void OnRecvPacket( ArraySegment<byte> buffer )
		{
			// 서버 패킷 처리.
			ushort packetSize = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
			ushort packetId = BitConverter.ToUInt16(buffer.Array, buffer.Offset+2);
			string msg = Encoding.UTF8.GetString(buffer.Array, buffer.Offset+4, packetSize-4);
			Console.WriteLine( $"[From Server] Packet ID: {packetId}, Size:{packetSize}, Message: {msg}" );
		}

		public override void OnSend( int bytes )
		{
			
		}
	}
}
