using Microsoft.Extensions.Logging;
using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Jobs
{
	public class MoveJob : AsyncRoomJob
	{
		private readonly Protocol.C_Move _movePacket;

		public override string JobType => "PlayerMove";
		public override int Priority => RoomJobPriority.Normal;
		public override int TimeoutMs => 3000;	// 이동 처리 타임아웃 3초

		public MoveJob(GameSession session, IRoom room, Protocol.C_Move movePacket, ILogger logger)
			:base(session, room, logger)
		{
			_movePacket = movePacket ?? throw new ArgumentNullException(nameof(movePacket));
		}

		protected override async Task ExecuteAsync()
		{
			try
			{
				_logger.LogDebug( "Processing move for Player {SessionId} in Room {RoomId} to position ({X}, {Y}. {Z})",
					_session.SessionId, _room.RoomId,
					_movePacket.PosInfo.PosX, _movePacket.PosInfo.PosY, _movePacket.PosInfo.PosZ );

				// Room의 HandlePlayerMoveAsync 호출
				await _room.HandlePlayerMoveAsync( _session, _movePacket );

				_logger.LogDebug( "Move processed successfully for Player {SessionId} in Room {RoomId}",
					  _session.SessionId, _room.RoomId );
			}
			catch( Exception ex )
			{
				_logger.LogError( ex, "Failed to process move for Player {SessionId} in Room {RoomId}",
					 _session.SessionId, _room.RoomId );
				throw; // 예외를 다시 던져서 기반 클래스에서 처리
			}
		}

		protected override bool PreExecutionValidation()
		{
			// 기본 검증 수행
			if(!base.PreExecutionValidation())
				return false;

			// MovePacket 유효성 검증
			if(_movePacket?.PosInfo == null)
			{
				_logger.LogWarning( "Invalid move packet for Player {SessionId} in Room {RoomId}: packet is null or missing position info",
					  _session.SessionId, _room.RoomId );
				return false;
			}

			// 위치 범위 검증 (기본적인 검증)
			PosInfo pos = _movePacket.PosInfo;
			if(float.IsNaN(pos.PosX) || float.IsNaN(pos.PosY) || float.IsNaN(pos.PosZ) ||
				float.IsInfinity(pos.PosX) || float.IsInfinity(pos.PosY) || float.IsInfinity(pos.PosZ))
			{
				_logger.LogWarning( "Invalid position values for Player {SessionId} in Room {RoomId}: ({X}, {Y}, {Z})",
									 _session.SessionId, _room.RoomId, pos.PosX, pos.PosY, pos.PosZ );
				return false;
			}

			return true;
		}

		protected override void HandleException( Exception exception )
		{
			// 이동 실패 시 특별한 처리가 필요한 경우 여기서 구현
			base.HandleException( exception );
		}

		public override string ToString()
		{
			PosInfo pos = _movePacket.PosInfo;
			if (pos != null)
			{
				return $"{base.ToString()} - Target Position: ({pos.PosX:F2}, {pos.PosY:F2}, {pos.PosZ:F2})";
			}
			return base.ToString();
		}
	}
}
