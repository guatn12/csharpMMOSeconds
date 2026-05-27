using System;
using System.Collections.Generic;

namespace Server.Core.Session
{
	public interface ISessionManager
	{
		#region 세션 생성
		ClientSession CreateSession();
		#endregion

		#region 세션 등록/해제
		bool RegisterSession( IClientSession session );
		bool UnregisterSession( long sessionId );
		#endregion

		#region 세션 조회
		IClientSession GetSession( long sessionId );
		IClientSession GetSessionByPlayerId( long playerId );
		#endregion

		#region 통계 및 전체 조회
		int GetTotalSessionCount();
		IEnumerable<IClientSession> GetAllActiveSessions();
		#endregion

		#region 이벤트
		event EventHandler<SessionRegisteredEventArgs> SessionRegistered;
		event EventHandler<SessionUnregisteredEventArgs> SessionUnregistered;
		event EventHandler<SessionDisconnectingEventArgs> SessionDisconnecting;
		#endregion

		#region 수명주기
		void Shutdown();
		/// <summary>
		/// 세션이 disconnect 절차에 진입했음을 lifecycle 구독자에게 알린다.
		/// NetworkSession.OnDisConnected 흐름에서만 호출하며, 일반 게임 로직은 호출 금지
		/// </summary>
		void NotifyDisconnecting( IClientSession session, DisconnectReason reason );
		#endregion

	}
}
