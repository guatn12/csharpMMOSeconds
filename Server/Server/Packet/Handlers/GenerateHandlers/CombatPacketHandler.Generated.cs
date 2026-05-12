// [자동 생성] CombatPacketHandler Dictionary 초기화
using Protocol;
using Google.Protobuf;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Server.Core.Session;
using Microsoft.Extensions.Logging;
//test

namespace Server.Packet.Handlers
{
	public partial class CombatPacketHandler : IPacketHandler
	{
		/// <summary>
		/// 테스트 및 디버깅용 Dictionary.
		/// 역직렬화된 IMessage 객체를 직접 처리할 때 사용.
		/// 런타임 패킷 처리는 _onRecv Dictionary 사용.
		/// </summary>
		public Dictionary<Type, Func<IClientSession, IMessage, Task>> Handlers {  get; private set; }
		private Dictionary<ushort, Func<IClientSession, ArraySegment<byte>, ValueTask>> _onRecv;

		private void InitializeHandlers()
		{
			Handlers = new Dictionary<Type, Func<IClientSession, IMessage, Task>>();
			_onRecv = new Dictionary<ushort, Func<IClientSession, ArraySegment<byte>, ValueTask>>();

			Handlers.Add(typeof(C_UseSkill), async (s, p) => await HandleC_UseSkillAsync( s, (C_UseSkill)p));
			_onRecv.Add((ushort)PacketID.C_UseSkill, HandleC_UseSkillAsync);
		}

		public async ValueTask HandleAsync(IClientSession session, ushort id, ArraySegment<byte> buffer)
		{
			if(_onRecv.TryGetValue(id, out var handler))
			{
				await handler(session, buffer);
			}
			else
			{
				_logger.LogWarning( "CombatPacketHandler _onRecv Dictionary Not Found id {id.ToString()}"  );
			}
		}

		private async ValueTask HandleC_UseSkillAsync(IClientSession session, ArraySegment<byte> buffer)
		{
			if(session.State != SessionState.InRoom)
			{
				_logger.LogDebug("Packet dropped in handler: SessionId={SessionId}, State={State}", session.SessionId, session.State);
				return;
			}
			var packet = new C_UseSkill();
			packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
			await HandleC_UseSkillAsync(session, packet);
		}
	}
}
