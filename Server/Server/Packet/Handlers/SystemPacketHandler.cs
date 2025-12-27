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
		//private readonly BaseRoom _room;

		public SystemPacketHandler(ILogger<SystemPacketHandler> logger)
		{
			_logger = logger;
			InitializeHandlers();
		}

		private async Task HandleC_EnterGameAsync(GameSession session, C_EnterGame packet)
		{
			// SessionManager가 자동으로 로비 배정을 처리
			// 여기서는 로깅만 수행
			_logger.LogDebug( "Player {PlayerId} (Session {SessionId}) entered game - handled by SessionManager",
				session.Player.PlayerId, session.SessionId );

			await Task.CompletedTask;
		}
	}
}
