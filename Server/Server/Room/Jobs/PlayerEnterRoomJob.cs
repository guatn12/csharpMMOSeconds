using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Jobs
{
	public class PlayerEnterRoomJob : AsyncRoomJob
	{
		public override string JobType => "PlayerEnterRoom";
		public override int Priority => RoomJobPriority.High;

		public PlayerEnterRoomJob( GameSession session, IRoom room, ILogger logger )
			: base( session, room, logger )
		{
		}

		protected override async Task ExecuteAsync()
		{
			_logger.LogInformation( "Processing room enter for Player {SessionId} into Room {RoomId}",
				_session.SessionId, _room.RoomId );

			var result = await _room.TryEnterAsync( _session );

			if(result == RoomEnterResult.Success)
			{
				// 입장 성공 패킷 전송 (필요시)
				var enterPacket = new Protocol.S_EnterGame
				{
					Player = new Protocol.PlayerInfo
					{
						PlayerId = _session.SessionId,
						Name = $"Player_{_session.SessionId}", // 임시 이름
						PosInfo = new Protocol.PosInfo{PosX = 0, PosY = 0, PosZ = 0}
					}
				};

				await _room.SendToPlayerAsync( _session, enterPacket );

				_logger.LogInformation( "Player {SessionId} successfully entered Room {RoomId}",
					_session.SessionId, _room.RoomId );
			}
			else
			{
				_logger.LogWarning( "Player {SessionId} failed to enter Room {RoomId}: {Result}",
					_session.SessionId, _room.RoomId, result );
			}
		}

		protected override bool PreExecutionValidation()
		{
			// 세션과 룸의 기본적인 유효성만 확인
			if(_session == null)
			{
				_logger.LogError( "Session is null in {JobType} job", JobType );
				return false;
			}

			if( _room == null )
			{
				_logger.LogError( "Room is null in {JobType} job for Session {SessionId}", JobType, _session.SessionId );
				return false;
			}

			// 룸 상태 확인
			if(_room.State == RoomState.Closed || _room.State == RoomState.Closing)
			{
				_logger.LogWarning( "Room {RoomId} is closing/closed, cannot enter for Session {SessionId}",
					 _room.RoomId, _session.SessionId );
				return false;
			}

			return true;
		}
	}
}
