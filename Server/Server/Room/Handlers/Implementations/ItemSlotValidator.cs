using Microsoft.Extensions.Logging;
using Protocol;
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
	/// 아이템 슬롯 검증 Validator
	/// </summary>
	public class ItemSlotValidator : IPacketValidator<C_UseItem>
	{
		/// <summary>
		/// Priority 10: 기본 검증(0) 이후 바로 실행
		/// </summary>
		public int Priority => 10;

		public Task<ValidationResult> ValidateAsync( GameSession session, C_UseItem packet, BaseRoom room, ILogger logger )
		{
			// 1. 슬롯 범위 검증
			if(packet.Slot < 0 || 50 <= packet.Slot)
			{
				logger.LogWarning( "잘못된 아이템 슬롯: Session={SessionId}, Slot={Slot}, 허용범위=[0,49]",
					session.SessionId, packet.Slot );
				return Task.FromResult( ValidationResult.Failure( $"잘못된 슬롯입니다. (입력: {packet.Slot}, 허용: 0~49" ) );
			}

			// 2. 수량 검증
			if(packet.Quantity <= 0)
			{
				logger.LogWarning("잘못된 아이템 수량: Session={SessionId}, Quantity={Quantity}", session.SessionId, packet.Quantity );

				return Task.FromResult( ValidationResult.Failure( "수량은 1 이상이어야 합니다." ) );
			}

			// 3. 최대 수량 제한
			if(999 < packet.Quantity)
			{
				logger.LogWarning( "과도한 아이템 수량: Session={SessionId}, Quantity={Quantity}", 
					session.SessionId, packet.Quantity );

				return Task.FromResult( ValidationResult.Failure( "수량은 999 이하여야 합니다." ) );
			}

			return Task.FromResult( ValidationResult.Success() );
		}
	}
}
