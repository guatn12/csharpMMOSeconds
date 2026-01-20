using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Room;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Packet.Handlers
{
	/// <summary>
	/// 시스템 관련 패킷 핸들러
	/// </summary>
	public partial class SystemPacketHandler
	{
		private readonly ILogger<SystemPacketHandler> _logger;
		private readonly IRoomManager _roomManager;

		public SystemPacketHandler(ILogger<SystemPacketHandler> logger, IRoomManager roomManager )
		{
			_logger = logger;
			_roomManager = roomManager;
			InitializeHandlers();
		}

		private async Task HandleC_EnterGameAsync( IClientSession session, C_EnterGame packet)
		{
			var currentRoom = session.CurrentRoom;
			if(currentRoom == null)
			{
				// 자동 로비 입장
				var result = await _roomManager.JoinDefaultLobbyAsync( session );
				if(result == RoomEnterResult.Success)
				{
					_logger.LogInformation( "Player {PlayerId} (Session {SessionId}) automatically joined the default lobby.",
						session.Player.PlayerId, session.SessionId );
				}
				else
				{
					_logger.LogWarning( "Player {PlayerId} (Session {SessionId}) failed to join the default lobby.",
						session.Player.PlayerId, session.SessionId );
				}
			}

			await Task.CompletedTask;
		}
	}
}
