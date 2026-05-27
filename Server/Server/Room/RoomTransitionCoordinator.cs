using Microsoft.Extensions.Logging;
using Server.Core.Session;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room
{
	public class RoomTransitionCoordinator : IRoomTransitionCoordinator, IDisposable
	{
		private readonly ILogger<RoomTransitionCoordinator> _logger;
		private readonly IRoomManager _roomManager;
		private readonly ISessionManager _sessionManager;
		private readonly ConcurrentDictionary<long, RoomTransitionContext> _activeTransitions = new();

		private const int TransitionTimeoutMs = 5000; // 5 seconds

		public RoomTransitionCoordinator(ILogger<RoomTransitionCoordinator> logger, IRoomManager roomManager, ISessionManager sessionManager)
		{
			_logger = logger;
			_roomManager = roomManager;
			_sessionManager = sessionManager;

			_sessionManager.SessionDisconnecting += OnSessionDisconnecting;
		}

		public async Task<RoomTransitionResult> ChangeRoomAsync( IClientSession session, int targetRoomId, RoomTransitionReason reason )
		{
			// Step1: Active transition 차단
			var context = new RoomTransitionContext
			{
				SessionId = session.SessionId,
				SourceRoomId = session.CurrentRoom.RoomId,
				TargetRoomId = targetRoomId,
				Reason = reason,
			};
			context.Cancellation.CancelAfter( TransitionTimeoutMs );

			if(_activeTransitions.TryAdd(session.SessionId, context) == false)
			{
				_logger.LogInformation( "[Transition:{TransitionId}] Rejected - already transferring. SessionId={SessionId}",
					context.TransitionId, session.SessionId );
				
				return RoomTransitionResult.AlreadyTransferring;
			}

			BaseRoom baseTargetRoom = null;
			BaseRoom baseSourceRoom = session.CurrentRoom as BaseRoom;
			bool reservationHeld = false;
			bool sourceProtected = false;

			try
			{
				// Step2: target room 유효성 검사
				context.Step = RoomTransitionStep.Validating;
				IRoom targetRoom = _roomManager.FindRoom(targetRoomId);
				if(targetRoom == null)
				{
					context.Step = RoomTransitionStep.Failed;
					return RoomTransitionResult.TargetNotFound;
				}

				if(targetRoom is not BaseRoom resolved)
				{
					context.Step = RoomTransitionStep.Failed;
					return RoomTransitionResult.UnknownError;
				}
				baseTargetRoom = resolved;

				if(context.Cancellation.IsCancellationRequested)
					return RoomTransitionResult.Cancelled;

				// Step3: target room 예약 (target + 1)
				context.Step = RoomTransitionStep.ReservingTarget;
				bool reserved = await baseTargetRoom.PushAsync<bool>(
					() => ValueTask.FromResult(baseTargetRoom.TryReserveEnter()));
				if(!reserved) return RoomTransitionResult.TargetFull;
				reservationHeld = true;

				// Step3.5: source 보호 (source +1)
				if(baseSourceRoom != null)
				{
					await baseSourceRoom.PushAsync( () =>
					{
						baseSourceRoom.BeginSourceProtection();
						return ValueTask.CompletedTask;
					} );
					sourceProtected = true;
				}

				if(context.Cancellation.IsCancellationRequested)
					return RoomTransitionResult.Cancelled;

				// Step4: source room 퇴장
				context.Step = RoomTransitionStep.LeavingSource;
				if(baseSourceRoom != null)
				{
					bool left = await baseSourceRoom.LeaveViaQueueAsync(session);
					if(!left)
					{
						context.Step = RoomTransitionStep.Failed;
						return RoomTransitionResult.UnknownError;
					}
				}

				// Step5: target room 입장 (예약 보유 상태)
				// push 직전에 release 책임을 잡으로 이전. EnterWithReservationAsync는 finally에서 경로(성공/실패/예외) 무관 예약을 1회 소진하므로
				// "잡이 돌면 반드시 release"가 성립. -> 이후 Coordinator finally는 Step 2 ~ 4 사이 실패만 담당 = 이중 해제 구조적 불가능.
				context.Step = RoomTransitionStep.EnteringTarget;
				reservationHeld = false;
				RoomEnterResult enterResult = await baseTargetRoom.PushAsync<RoomEnterResult>(
					() => baseTargetRoom.EnterWithReservationAsync(session));
				if(enterResult != RoomEnterResult.Success)
				{
					// 입장 실패 시 롤백 처리 - TODO
					context.Step = RoomTransitionStep.RollingBack;
					return await TryRollBackAsync( session, baseSourceRoom, enterResult );
				}

				context.Step = RoomTransitionStep.Completed;
				_logger.LogInformation( "[Transition:{TransitionId}] Completed. SessionId={SessionId} {Source} => {Target} ({Reason})",
					context.TransitionId, session.SessionId, context.SourceRoomId, context.TargetRoomId, context.Reason );

				return RoomTransitionResult.Success;
			}
			finally
			{
				// Reservation 미consume 상태로 흐름 종료 시 해제(실패 경로)
				if(reservationHeld && baseTargetRoom != null)
				{
					await baseTargetRoom.PushAsync( () =>
					{
						baseTargetRoom.ReleaseEnterReservation();
						return ValueTask.CompletedTask;
					} );
				}

				// source 보호 해제 (rollback 시도까지 끝난 후)
				if(sourceProtected && baseSourceRoom != null)
				{
					await baseSourceRoom.PushAsync( () =>
					{
						baseSourceRoom.EndSourceProtection();
						return ValueTask.CompletedTask;
					} );
				}

				_activeTransitions.TryRemove( session.SessionId, out _ );
				context.Cancellation.Dispose();
			}
		}

		public bool TryGetActiveTransition( long sessionId, out RoomTransitionContext context )
			=> _activeTransitions.TryGetValue( sessionId, out context );

		public bool CancelTransition( long sessionId, RoomTransitionCancelReason reason )
		{
			if(_activeTransitions.TryGetValue( sessionId, out var context ) == false)
				return false;

			try
			{
				context.Cancellation.Cancel();
				_logger.LogInformation( "[Transition:{TransitionId}] Cancelled by {Reason}. SessionId={SessionId}, Step={Step}",
					context.TransitionId, reason, sessionId, context.Step );
				return true;
			}
			catch(ObjectDisposedException)
			{
				// 이미 종료된 transition의 CancellationTokenSource에 접근한 경우
				return false;
			}
		}

		private void OnSessionDisconnecting(object sender, SessionDisconnectingEventArgs e)
		{
			CancelTransition( e.SessionId, RoomTransitionCancelReason.Disconnect );
		}

		private async Task<RoomTransitionResult> TryRollBackAsync(IClientSession session, BaseRoom sourceRoom, RoomEnterResult failedResult)
		{
			if(sourceRoom == null)
			{
				// 2안: roomless 격리
				session.SetCurrentRoom( null );
				_logger.LogWarning( "Rollback fallback to roomless. SessionId={SessionId} failedResult={FailedResult}",
					session.SessionId, failedResult );
				return RoomTransitionResult.RollbackFailed;
			}

			// 1안: source 재입장 시도
			RoomEnterResult rollbackResult = await sourceRoom.EnterViaQueueAsync(session);
			if(rollbackResult == RoomEnterResult.Success)
			{
				_logger.LogInformation( "Rollback succeeded. SessionId={SessionId} returned to Room={RoomId}",
					session.SessionId, sourceRoom.RoomId );
				return RoomTransitionResult.RollbackSucceeded;
			}

			// 1안 실패 -> 2안 fallback
			session.SetCurrentRoom( null );
			_logger.LogError( "Rollback failed. SessionId={sessionId}, sourceRoom={sourceRoomId}, RollbackResult={RollbackResult}",
				session.SessionId, sourceRoom.RoomId, failedResult );
			return RoomTransitionResult.RollbackFailed;
		}

		private static RoomTransitionResult MapEnterResultToTransitionResult(RoomEnterResult result)
		{
			return result switch
			{
				RoomEnterResult.RoomClosed => RoomTransitionResult.TargetClosed,
				RoomEnterResult.RoomFull => RoomTransitionResult.TargetFull,
				RoomEnterResult.AlreadyInRoom => RoomTransitionResult.Rejected,
				_ => RoomTransitionResult.UnknownError,

			};
		}

		public void Dispose()
		{
			_sessionManager.SessionDisconnecting -= OnSessionDisconnecting;
		}
	}
}
