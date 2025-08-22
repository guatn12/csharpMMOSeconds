using Microsoft.Extensions.Logging;
using Server.Room.Jobs;
using System;
using System.Threading.Tasks;

namespace Server.Room.Jobs
{
	public class RoomCleanupJob : AsyncRoomJob
	{
		public override string JobType => "RoomCleanup";
		public override int Priority => RoomJobPriority.VeryLow; // 낮은 우선순위
		public override int TimeoutMs => 10000; // 정리 작업 타임아웃 10초

		public RoomCleanupJob( IRoom room, ILogger logger )
			: base( null, room, logger ) // session은 null (시스템 작업)
		{
		}

		protected override async Task ExecuteAsync()
		{
			try
			{
				_logger.LogDebug( "Starting cleanup for Room {RoomId} '{RoomName}'",
					_room.RoomId, _room.RoomName );

				// 비활성 세션 정리
				await CleanupInactiveSessionsAsync();

				// 룸 상태 점검
				await ValidateRoomStateAsync();

				_logger.LogDebug( "Cleanup completed for Room {RoomId} '{RoomName}'",
					_room.RoomId, _room.RoomName );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Failed to cleanup Room {RoomId} '{RoomName}'",
					_room.RoomId, _room.RoomName );
				throw;
			}
		}

		protected override bool PreExecutionValidation()
		{
			// 세션 검증 건너뛰기 (시스템 작업이므로)
			if(_room == null)
			{
				_logger.LogError( "Room is null in {JobType} job", JobType );
				return false;
			}

			if(_room.State == RoomState.Closed || _room.State == RoomState.Closing)
			{
				_logger.LogDebug( "Room {RoomId} is closing/closed, skipping cleanup", _room.RoomId );
				return false;
			}

			return true;
		}

		private async Task CleanupInactiveSessionsAsync()
		{
			// 비활성 세션 정리 로직
			// 실제 구현에서는 세션 상태를 확인하고 비활성 세션을 제거
			await Task.CompletedTask;
		}

		private async Task ValidateRoomStateAsync()
		{
			// 룸 상태 검증 로직
			// 실제 구현에서는 룸의 일관성을 확인하고 필요시 수정
			await Task.CompletedTask;
		}
	}
}
