using Google.Protobuf;
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
		public int SessionId { get; private set; }

		public void Send(IMessage packet)
		{
			ArraySegment<byte> segment = Program.PacketManagerInstance.MakeSendPacket(packet);

			base.Send( segment );
		}

		public override void OnConnected( EndPoint endPoint )
		{
			Console.WriteLine( $"[Connected] {endPoint}" );
			//Send( 2, Encoding.UTF8.GetBytes( "Welcome to MMORPG Server" ) );
			// TODO : 클라이언트에게 입장 패킷 전송.
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			Console.WriteLine( $"[Disconnected] {endPoint}" );
		}

		public override void OnRecvPacket( ArraySegment<byte> buffer )
		{
			Program.PacketManagerInstance.HandlePacket( this, buffer );
		}

		public override void OnSend( int bytes )
		{
			Console.WriteLine( $"[Send] {bytes} bytes" );
		}
	}
}
