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
	/// 브로드캐스트 전송 전략 구현
	/// </summary>
	public class BroadcastResponder<TResponse> : IPacketResponder<TResponse> where TResponse : IMessage
	{
		/// <summary>
		/// 본인 포함 여부
		/// </summary>
		private readonly bool _includeSelf;

		/// <summary>
		/// 생성자
		/// </summary>
		public BroadcastResponder(bool includeSelf = false)
		{
			_includeSelf = includeSelf;
		}

		/// <summary>
		/// 룸 전체에 브로드캐스트
		/// </summary>
		public async Task SendAsync( GameSession session, TResponse response, BaseRoom room, ILogger logger )
		{
			try
			{
				// 조건부 브로드캐스트
				if(_includeSelf)
				{
					// 본인 포함
					await room.BroadcastAsync( response );
					logger.LogDebug( "Broadcast(본인 포함): Room={RoomId}, Packet={PacketType}, PlayerCount={Count}",
						room.RoomId, typeof( TResponse ).Name, room.CurrentPlayerCount );
				}
				else
				{
					// 본인 제외
					await room.BroadcastAsync( response, session );
					logger.LogDebug( "Broadcast(본인 제외): Room={RoomId}, Packet={PacketType}, PlayerCount={Count}",
						room.RoomId, typeof( TResponse ).Name, room.CurrentPlayerCount - 1 );
				}
			}
			catch ( Exception ex )
			{
				// 오류 처리
				logger.LogError( ex, "Broadcast 실패: RoomId={RoomId}, Packet={PacketType}",
					room.RoomId, typeof( TResponse ).Name );

				// 브로드캐스트는 일부 실패해도 계속 진행
			}
		}
	}
}
