using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Server.Room;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Jobs
{
	public class PacketJob<T> : IPacketJob<T> where T : IMessage
	{
		private GameSession _session;
		private IRoom _room;
		private T _packet;
		private ILogger _logger;
		private Func<GameSession, IRoom, IMessage, ILogger, ValueTask> _handler;

		public void Initialize( GameSession session, IRoom room, T packet, ILogger logger )
		{
			_session = session;
			_room = room;
			_packet = packet;
			_logger = logger;
		}

		public void SetHandler( Func<GameSession, IRoom, IMessage, ILogger, ValueTask> handler )
		{
			_handler = handler ?? throw new ArgumentNullException( nameof( handler ) );
		}

		public async void Execute()
		{
			if(_handler == null)
			{
				_logger.LogWarning( "PacketJob<{PacketType}> executed without handler", typeof( T ).Name );
				return;
			}

			await _handler( _session, _room, _packet, _logger );
		}

		public void Clear()
		{
			_session = null;
			_room = null;
			_packet = default( T );
			_logger = null;
			_handler = null;
		}
	}
}
