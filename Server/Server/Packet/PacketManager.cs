using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Protobuf;

namespace Server.Packet
{
	public partial class PacketManager
	{
		public void HandlePacket(PacketSession session, ArraySegment<byte> buffer)
		{
			ushort count = 0;

			ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
			count += 2;
			ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);
			count += 2;

			if(_onRecv.TryGetValue(id, out var action))
			{
				action.Invoke( session, new ArraySegment<byte>( buffer.Array, buffer.Offset + count, size - count ) );
			}
		}

		public void Send(PacketSession session, IMessage packet)
		{
			ushort packetId = (ushort)Enum.Parse<PacketID>(packet.Descriptor.Name);
			if(_makePacket.TryGetValue(packetId, out var func))
			{
				ArraySegment<byte> segment = func.Invoke( packet);
				session.Send( segment );
			}
		}
	}
}
