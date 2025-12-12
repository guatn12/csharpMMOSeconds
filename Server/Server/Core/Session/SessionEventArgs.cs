using System;

namespace Server.Core.Session
{
	/// <summary>
	/// 세션 등록 이벤트 데이터
	/// </summary>
	public class SessionRegisteredEventArgs : EventArgs
	{
		public long SessionId { get; init; }
		public long PlayerId { get; init; }
		public DateTime RegisteredAt { get; init; }
	}

	/// <summary>
	/// 세션 해제 이벤트 데이터
	/// </summary>
	public class SessionUnregisteredEventArgs : EventArgs
	{
		public long SessionId { get; init; }
		public long PlayerId { get; init; }
		public DateTime UnregisteredAt { get; init; }
		public string Reason { get; init; }
	}
}
