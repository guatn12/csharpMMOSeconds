using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Jobs
{
	public class PlayerLeaveRoomJob : AsyncRoomJob
	{
		public override string JobType => "PlayerLeaveRoom";
		public override int Priority => RoomJobPriority.High;

		public PlayerLeaveRoomJob( GameSession session, IRoom room, ILogger logger )
			  : base( session, room, logger )
		{
		}

		protected override async Task ExecuteAsync()
		{
			_logger.LogInformation( "Processing room leave for Player {SessionId} from Room {RoomId}",
				_session.SessionId, _room.RoomId );

			var success = await _room.TryLeaveAsync(_session);

			if(success)
			{
				// 퇴장 알림 패킷 전송 (필요시)
				var leavePacket = new Protocol.S_LeaveGame
				{
					PlayerId = _session.SessionId
				};

				// 남은 플레이어들에게 퇴장 알림
				await _room.BroadcastAsync( leavePacket );

				_logger.LogInformation( "Player {SessionId} successfully left Room {RoomId}",
					_session.SessionId, _room.RoomId );
			}
			else
			{
				_logger.LogWarning( "Player {SessionId} failed to leave Room {RoomId}",
					_session.SessionId, _room.RoomId );
			}
		}
	}
}
