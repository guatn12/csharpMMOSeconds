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
	/// 기본 패킷 검증 전략 구현
	/// </summary>
	public class BasicPacketValidator<TRequest> : IPacketValidator<TRequest> where TRequest : IMessage
	{
		/// <summary>
		/// 우선 순위 0 (가장 먼저 실행)
		/// </summary>
		public int Priority => 0;

		/// <summary>
		/// 기본 검증 실행
		/// </summary>
		public Task<ValidationResult> ValidateAsync( GameSession session, TRequest packet, BaseRoom room, ILogger logger )
		{
			// 1. 세션 NULL 체크
			if(session == null)
			{
				logger.LogWarning( "BasicValidator: 세션이 null입니다." );
				return Task.FromResult( ValidationResult.Failure( "세션이 없습니다." ) );
			}

			// 2. 세션이 룸에 포함되어 있는지 확인
			if(!room.ContainsPlayer( session ))
			{
				logger.LogWarning( "BasicValidator: 세션{SessionId}가 룸{RoomId}에 없습니다.", session.SessionId, room.RoomId );
				return Task.FromResult( ValidationResult.Failure( "룸에 플레이어가 없습니다." ) );
			}

			// 3. 패킷 NULL 체크
			if(packet == null)
			{
				logger.LogWarning( "BasicValidator: 패킷이 null입니다. Session={SessionId}", session.SessionId );
				return Task.FromResult( ValidationResult.Failure( "패킷이 null입니다." ) );
			}

			return Task.FromResult( ValidationResult.Success() );
		}
	}
}
