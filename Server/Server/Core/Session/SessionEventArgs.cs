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

	public class SessionDisconnectingEventArgs : EventArgs
	{
		public long SessionId { get; init; }
		public long PlayerId { get; init; }
		public DisconnectReason Reason { get; init; }
		public DateTime DisconnectingAt { get; init; }
	}

	public enum DisconnectReason
	{
		ClientDisconnect = 0,	//  클라이언트 측 종료(소켓 close, 네트워크 단절 등)
		Timeout			 = 1,	//	서버 측 hearbeat timeout
		ServerShutdown	 = 2,	//	서버 종료에 의한 disconnect
		Forced			 = 3,	//	관리자 kick / anti-cheat ban 등 강제
	}

}
