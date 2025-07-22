using Google.Protobuf;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace DummyClient
{
	public class ServerSession : Session
	{
		public int DummyId { get; private set; }

		public void Send( IMessage packet )
		{
			ArraySegment<byte> segment = Program.PacketManagerInstance.MakeSendPacket(packet);

			base.Send( segment );
		}

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
			Program.PacketManagerInstance.HandlePacket( this, buffer );
		}

		public override void OnSend( int bytes )
		{
			
		}
	}
}
