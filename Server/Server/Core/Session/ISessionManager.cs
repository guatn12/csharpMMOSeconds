using System;
using System.Collections.Generic;

namespace Server.Core.Session
{
	public interface ISessionManager
	{
		#region 세션 생성
		GameSession CreateSession();
		#endregion

		#region 세션 등록/해제
		bool RegisterSession( GameSession session );
		bool UnregisterSession( long sessionId );
		#endregion

		#region 세션 조회
		GameSession GetSession( long sessionId );
		GameSession GetSessionByPlayerId( long playerId );
		#endregion

		#region 통계 및 전체 조회
		int GetTotalSessionCount();
		IEnumerable<GameSession> GetAllActiveSessions();
		#endregion

		#region 이벤트
		event EventHandler<SessionRegisteredEventArgs> SessionRegistered;
		event EventHandler<SessionUnregisteredEventArgs> SessionUnregistered;
		#endregion



	}
}
