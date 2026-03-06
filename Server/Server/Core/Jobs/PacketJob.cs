using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Server.Core.Session;
using Server.Packet.Handlers;
using Server.Room;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core.Jobs
{
	public class PacketJob : IJob
	{
		private IPacketHandler _handler;
		private IClientSession _session;
		private ushort _packetId;
		private byte[] _packetData;

		public void Initialize( IPacketHandler packetHandler, IClientSession gameSession, ushort packetId, ArraySegment<byte> buffer )
		{
			_session = gameSession;
			_handler = packetHandler;
			_packetId = packetId;

			_packetData = new byte[ buffer.Count];
			Array.Copy( buffer.Array, buffer.Offset, _packetData, 0, buffer.Count );
		}

		public async ValueTask ExecuteAsync()
		{
			var packetData = new ArraySegment<byte>( _packetData );
			await _handler.HandleAsync( _session, _packetId, packetData );
		}

		public void Clear()
		{
			_handler = null;
			_session = null;
			_packetId = 0;
			_packetData = null;
		}
	}
}
