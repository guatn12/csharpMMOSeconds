// [자동 생성] RoomPacketHandler Dictionary 초기화
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
	public partial class RoomPacketHandler : IPacketHandler
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

			Handlers.Add(typeof(C_Move), async (s, p) => await HandleC_MoveAsync( s, (C_Move)p));
			_onRecv.Add((ushort)PacketID.C_Move, HandleC_MoveAsync);
			Handlers.Add(typeof(C_Chat), async (s, p) => await HandleC_ChatAsync( s, (C_Chat)p));
			_onRecv.Add((ushort)PacketID.C_Chat, HandleC_ChatAsync);
			Handlers.Add(typeof(C_PlayerInfo), async (s, p) => await HandleC_PlayerInfoAsync( s, (C_PlayerInfo)p));
			_onRecv.Add((ushort)PacketID.C_PlayerInfo, HandleC_PlayerInfoAsync);
		}

		public async ValueTask HandleAsync(IClientSession session, ushort id, ArraySegment<byte> buffer)
		{
			if(_onRecv.TryGetValue(id, out var handler))
			{
				await handler(session, buffer);
			}
			else
			{
				_logger.LogWarning( "RoomPacketHandler _onRecv Dictionary Not Found id {id.ToString()}"  );
			}
		}
		private async ValueTask HandleC_MoveAsync(IClientSession session, ArraySegment<byte> buffer)
		{
			var packet = new C_Move();
			packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
			await HandleC_MoveAsync(session, packet);
		}
		private async ValueTask HandleC_ChatAsync(IClientSession session, ArraySegment<byte> buffer)
		{
			var packet = new C_Chat();
			packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
			await HandleC_ChatAsync(session, packet);
		}
		private async ValueTask HandleC_PlayerInfoAsync(IClientSession session, ArraySegment<byte> buffer)
		{
			var packet = new C_PlayerInfo();
			packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
			await HandleC_PlayerInfoAsync(session, packet);
		}
	}
}
