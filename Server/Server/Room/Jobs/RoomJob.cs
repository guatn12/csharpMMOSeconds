using Microsoft.Extensions.Logging;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Jobs
{
	public abstract class RoomJob : IJob
	{
		protected readonly GameSession _session;
		protected readonly IRoom _room;
		protected readonly ILogger _logger;
		protected readonly DateTime _createdAt;
		private readonly Stopwatch _stopwatch;

		public GameSession Session => _session;
		public IRoom Room => _room;
		public DateTime CreatedAt => _createdAt;

		public abstract string JobType { get; }

		public virtual int Priority => 0;

		public virtual int TimeoutMs => 5000;

		protected RoomJob(GameSession session, IRoom room, ILogger logger)
		{
			_session = session ?? throw new ArgumentNullException(nameof(session));
			_room = room ?? throw new ArgumentNullException(nameof(room));
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_createdAt = DateTime.UtcNow;
			_stopwatch = new Stopwatch();
		}

		public void Execute()
		{
			var executionId = Guid.NewGuid().ToString("N")[..8]; // 실행 추적용 짧은 ID

			try
			{
				_stopwatch.Start();

				// job 실행 전 검증
				if(!PreExecutionValidation())
				{
					_logger.LogWarning( "Job {JobType} ({ExecutionId}) pre-execution validation failed for session {SessionId} in Room {RoomId}",
						JobType, executionId, _session.SessionId, _room.RoomId );
					return;
				}

				_logger.LogDebug( "Executing {JobType} job ({ExecutionId}) for session {SessionId} in Room {RoomId}",
					JobType, executionId, _session.SessionId, _room.RoomId );

				// 실제 job 로직 실행
				ExecuteInternal();

				_stopwatch.Stop();
				var executionTime = _stopwatch.ElapsedMilliseconds;
				// 성공 로그
				_logger.LogInformation("Job {JobType} ({ExecutionId}) completed successfully in {ExecutionTime}ms for Session {SessionId} in Room {RoomId}",
					JobType, executionId, executionTime, _session.SessionId, _room.RoomId);

				// 실행 시간이 긴 경우 경고
				if(executionTime > 1000)
				{
					_logger.LogWarning( "Job {JobType} ({ExecutionId}) took {ExecutionTime}ms to complete, consider optimization",
						JobType, executionId, executionTime );
				}
			}
			catch(TimeoutException)
			{
				_stopwatch.Stop();
				_logger.LogError("Job {JobType} ({ExecutionId}) timed out after {ElapsedTime}ms for Session {SessionId} in Room {RoomId}",
					JobType, executionId, _stopwatch.ElapsedMilliseconds, _session.SessionId, _room.RoomId );

				HandleTimeout();
			}
			catch(Exception ex)
			{
				_stopwatch.Stop();
				_logger.LogError( ex, "Job {JobType} ({ExecutionId}) failed after {ElapsedTime}ms for Session {SessionId} in Room {RoomId}",
					JobType, executionId, _stopwatch.ElapsedMilliseconds, _session.SessionId, _room.RoomId );

				HandleException( ex );
			}
			finally
			{
				// Job 실행 후 정리
				PostExecutionCleanup();
			}
		}

		protected abstract void ExecuteInternal();

		protected virtual bool PreExecutionValidation()
		{
			// 세션 유효성 검사
			if(_session == null)
			{
				_logger.LogError( "Session is null in {JobType} job", JobType );
				return false;
			}

			// 룸 유효성 검사
			if(_room == null)
			{
				_logger.LogError( "Room is null in {JobType} job for Session {SessionId}", JobType, _session.SessionId );
				return false;
			}

			// 플레이어가 여전히 룸에 있는지 확인
			if(!_room.ContainsPlayer(_session))
			{
				_logger.LogWarning( "Player {SessionId} is no longer in room {RoomId} for {JobType} job",
					  _session.SessionId, _room.RoomId, JobType );
				return false;
			}

			// 룸 상태 확인
			if(_room.State == RoomState.Closed || _room.State == RoomState.Closing)
			{
				_logger.LogWarning( "Room {RoomId} is closing/closed, skipping {JobType} job for Session {SessionId}",
					  _room.RoomId, JobType, _session.SessionId );
				return false;
			}

			return true;
		}

		protected virtual void PostExecutionCleanup()
		{
			// 기본 구현은 아무것도 하지 않음
			// 하위 클래스에서 필요에 따라 리소스 정리 등을 구현
		}

		protected virtual void HandleTimeout()
		{

		}

		protected virtual void HandleException(Exception exception)
		{

		}

		protected long GetElapsedMilliseconds()
		{
			return _stopwatch.ElapsedMilliseconds;
		}

		protected bool IsWithinTimeLimit(int maxMilliseconds)
		{
			return _stopwatch.ElapsedMilliseconds <= maxMilliseconds;
		}

		public override string ToString()
		{
			return $"{JobType} Job (Session: {_session?.SessionId}, Room: {_room?.RoomId}, Created: {_createdAt:yyyy-MM-dd HH:mm:ss.fff})";
		}
	}

	// 비동기 작업을 위한 RoomJob 기반 클래스
	public abstract class AsyncRoomJob : RoomJob
	{
		protected AsyncRoomJob( GameSession session, IRoom room, ILogger logger )
			: base( session, room, logger )
		{ }

		protected override void ExecuteInternal()
		{
			// 비동기 메서드를 동기적으로 실행
			// Job Queue는 동기 메서드만 지원하므로 이 방식 사용
			ExecuteAsync().GetAwaiter().GetResult();
		}

		protected abstract Task ExecuteAsync();
	}
}
