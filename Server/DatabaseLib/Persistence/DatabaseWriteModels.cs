
namespace DatabaseLib.Persistence
{
	public enum DatabaseWriteStatus
	{
		// db 트랜잭션 정상 커밋
		Committed = 0,
		// 요청 거부
		Rejected = 1,
		// Version 동시성 검사 실패
		ConcurrencyConflict = 2,
		// DB 작업 실패
		Failed = 3,
		// 알수 없는 예외
		OutcomeUnknown = 4,
		// Channel에 수락되전 대기 취소
		Cancelled = 5,
		// 종료 중
		QueueStopping = 6,
		// Critical 요청이 Channel 수락을 기다리다 timeout됨
		EnqueueTimeout = 7,
	}

	public enum DatabaseWriteEnqueueStatus
	{
		// queue에 들어감
		Accepted = 0,
		// queue가 꽉 참
		QueueFull = 1,
		// queue 종료 중
		QueueStopping = 2,
	}

	public readonly record struct DatabaseWriteDecision(bool ShouldCommit, string ErrorCode)
	{
		public static DatabaseWriteDecision Commit()
			=> new DatabaseWriteDecision( true, string.Empty );

		public static DatabaseWriteDecision Reject( string errorCode )
			=> new DatabaseWriteDecision( false, errorCode ?? "Rejected" );
	}

	public sealed class DatabaseWriteResult<TResult>
	{
		public DatabaseWriteStatus Status { get; }
		public bool HasValue { get; }
		public TResult Value { get; }
		public string ErrorCode { get; }

		private DatabaseWriteResult(DatabaseWriteStatus status, bool hasValue, TResult value, string errorCode)
		{
			Status = status;
			HasValue = hasValue;
			Value = value;
			ErrorCode = errorCode ?? string.Empty;
		}

		public static DatabaseWriteResult<TResult> Committed( TResult value )
			=> new DatabaseWriteResult<TResult>( DatabaseWriteStatus.Committed, true, value, string.Empty );

		public static DatabaseWriteResult<TResult> WithoutValue( DatabaseWriteStatus status, string errorCode )
			=> new DatabaseWriteResult<TResult>( status, false, default, errorCode );
	}
}
