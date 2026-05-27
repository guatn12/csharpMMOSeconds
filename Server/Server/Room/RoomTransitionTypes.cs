using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Room
{
	public enum RoomTransitionStep
	{
		Created = 0,
		Validating = 1,
		ReservingTarget = 2,
		LeavingSource = 3,
		EnteringTarget = 4,
		Completed = 5,
		RollingBack = 6,
		Cancelled = 7,
		Failed = 8,
	}

	public enum RoomTransitionResult
	{
		Success = 0,
		Rejected,
		TargetNotFound,
		TargetClosed,
		TargetFull,
		AlreadyTransferring,
		Cancelled,
		RollbackSucceeded,
		RollbackFailed,
		UnknownError,
	}

	public enum RoomTransitionReason
	{
		PlayerRequest = 0,
		Respawn,
		ServerTransfer,
		AdminMove,
		Matchmaking,
		Recovery,
	}

	public enum RoomTransitionCancelReason
	{
		Disconnect = 0,
		Shutdown,
		Timeout,
		SessionReplaced,
		HigherPriorityTransition,
	}

	public sealed class RoomTransitionContext
	{
		public Guid TransitionId { get; init; } = Guid.NewGuid();
		public long SessionId { get; init; }
		public int SourceRoomId { get; init; }
		public int TargetRoomId { get; init; }
		public RoomTransitionStep Step { get; set; } = RoomTransitionStep.Created;
		public RoomTransitionReason Reason { get; init; }
		public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
		public CancellationTokenSource Cancellation { get; init; } = new CancellationTokenSource();
	}
}
