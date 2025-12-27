// [자동 생성] IPacketHandler 인터페이스
using System;
using System.Threading.Tasks;
using Server.Core.Session;

namespace Server.Packet.Handlers
{
	public interface IPacketHandler
	{
		ValueTask HandleAsync(GameSession session, ushort id, ArraySegment<byte> buffer);
	}
}