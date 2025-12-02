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
	/// 플레이어 정보 조회 패킷 핸들러
	/// </summary>
	public class PlayerInfoPacketHandler : PacketHandlerBase<C_PlayerInfo, S_PlayerStat>
	{
		#region 생성자
		public PlayerInfoPacketHandler(BaseRoom room)
			:base(room, validators: null, responder: new SendToPlayerResponder<S_PlayerStat>())
		{ }
		#endregion


		#region Virtual 메서드 정의 (선택)
		protected override IEnumerable<IPacketValidator<C_PlayerInfo>> GetDefaultValidators()
		{
			return new List<IPacketValidator<C_PlayerInfo>>
			{
				new BasicPacketValidator<C_PlayerInfo>()
			};
		}
		#endregion

		#region Abstract 메서드 정의 (필수)
		protected override Task<S_PlayerStat> BuildResponseAsync( GameSession session, C_PlayerInfo packet, PacketProcessResult result, ILogger logger )
		{
			var response = new S_PlayerStat
			{
				Player = session.Player.Info
			};

			return Task.FromResult( response );
		}

		protected override Task<PacketProcessResult> ProcessPacketAsync( GameSession session, C_PlayerInfo packet, ILogger logger )
		{
			if(session.Player?.Info == null)
			{
				logger.LogWarning( "플레이어 정보 없음: Session={SessionId}", session.SessionId );

				return Task.FromResult( PacketProcessResult.Fail( "플레이어 정보가 존재하지 않습니다." ) );
			}

			logger.LogDebug( "플레이어 정보 조회: Session={SessionId}, PlayerName={PlayerName}, Level={Level}",
				session.SessionId, session.Player.Info.Name, session.Player.Info.Level);

			return Task.FromResult( PacketProcessResult.Ok() );
		}
		#endregion
	}
}
