using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Server.Core.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Handlers.Strategies
{
	/// <summary>
	/// 패킷 응답 전송 전략 인터페이스
	/// </summary>
	public interface IPacketResponder<TResponse> where TResponse : IMessage
	{
		/// <summary>
		/// 응답 패킷 전송.
		/// </summary>
		Task SendAsync( GameSession session, TResponse response, BaseRoom room, ILogger logger );
	}
}
