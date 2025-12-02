using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Server.Core.Session;
using Server.Room.Handlers.Strategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Handlers.Implementations
{
	/// <summary>
	/// 단일 플레이어 전송 전략 구현
	/// </summary>
	public class SendToPlayerResponder<TResponse> : IPacketResponder<TResponse> where TResponse : IMessage
	{
		/// <summary>
		/// 단일 플레이어에게 응답 전송
		/// </summary>
		public async Task SendAsync( GameSession session, TResponse response, BaseRoom room, ILogger logger )
		{
			try
			{
				await room.SendToPlayerAsync( session, response );

				// 성공 로그
				logger.LogDebug( "SendToPlayer: Session={SessionId}, Packet={PacketType}", session.SessionId, typeof( TResponse ).Name );
			}
			catch ( Exception ex )
			{
				// 오류 처리
				logger.LogError(ex, "SendToPlayer 실패: Session={SessionId}, Packet={PacketType}",
					session.SessionId, typeof( TResponse ).Name );

				// 오류 처리 필요시 Throw
			}
		}
	}
}
