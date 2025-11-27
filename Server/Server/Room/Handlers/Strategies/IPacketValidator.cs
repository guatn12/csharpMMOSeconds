using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Server.Core.Session;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Handlers.Strategies
{
	/// <summary>
	/// 패킷 검증 전략 인터페이스
	/// </summary>
	public interface IPacketValidator<TRequest> where TRequest : IMessage
	{
		// 검증 우선 순위
		int Priority { get; }

		// 패킷 검증
		Task<ValidationResult> ValidateAsync( GameSession session, TRequest packet, BaseRoom room, ILogger logger );
	}
}
