using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Room.Handlers.Implementations;
using Server.Room.Handlers.Strategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Handlers.Concrete
{
	/// <summary>
	/// 채팅 패킷 핸들러
	/// </summary>
	public class ChatPacketHandler : PacketHandlerBase<C_Chat, S_Chat>
	{
		#region 생성자
		public ChatPacketHandler(BaseRoom room)
			: base(room, validators: null, responder: new BroadcastResponder<S_Chat>(includeSelf: true))
		{

		}
		#endregion

		#region Abstract 메서드 구현 (필수)
		protected override Task<PacketProcessResult> ProcessPacketAsync( GameSession session, C_Chat packet, ILogger logger )
		{
			// 1. 메세지 검증
			if(string.IsNullOrWhiteSpace( packet.Message ))
			{
				return Task.FromResult( PacketProcessResult.Fail( "메세지가 비어있습니다." ) );
			}

			if(500 < packet.Message.Length)
			{
				return Task.FromResult( PacketProcessResult.Fail( "메세지가 너무 깁니다. (최대 500자)" ) );
			}

			// 2. 채팅 처리(간단)

			// 3. 성공 반환
			return Task.FromResult( PacketProcessResult.Ok( packet.Message ) );
		}

		protected override Task<S_Chat> BuildResponseAsync( GameSession session, C_Chat packet, PacketProcessResult result, ILogger logger )
		{
			var response = new S_Chat
			{
				PlayerId = session.PlayerId,
				Message = result.Data as string ?? packet.Message // Data를 string으로 캐스팅
			};

			return Task.FromResult( response );
		}
		#endregion

		#region Vritual 메서드 재정의(선택)
		protected override S_Chat CreateErrorResponse( string errorMessage )
		{
			return new S_Chat
			{
				PlayerId = -1,
				Message = $"[오류] {errorMessage}"
			};
		}
		#endregion
	}
}
