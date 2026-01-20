// [자동 생성] InventoryPacketHandler Dictionary 초기화
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
	public partial class InventoryPacketHandler : IPacketHandler
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

			Handlers.Add(typeof(C_InventoryRequest), async (s, p) => await HandleC_InventoryRequestAsync( s, (C_InventoryRequest)p));
			_onRecv.Add((ushort)PacketID.C_InventoryRequest, HandleC_InventoryRequestAsync);
			Handlers.Add(typeof(C_UseItem), async (s, p) => await HandleC_UseItemAsync( s, (C_UseItem)p));
			_onRecv.Add((ushort)PacketID.C_UseItem, HandleC_UseItemAsync);
			Handlers.Add(typeof(C_EquipItem), async (s, p) => await HandleC_EquipItemAsync( s, (C_EquipItem)p));
			_onRecv.Add((ushort)PacketID.C_EquipItem, HandleC_EquipItemAsync);
			Handlers.Add(typeof(C_UnequipItem), async (s, p) => await HandleC_UnequipItemAsync( s, (C_UnequipItem)p));
			_onRecv.Add((ushort)PacketID.C_UnequipItem, HandleC_UnequipItemAsync);
		}

		public async ValueTask HandleAsync(IClientSession session, ushort id, ArraySegment<byte> buffer)
		{
			if(_onRecv.TryGetValue(id, out var handler))
			{
				await handler(session, buffer);
			}
			else
			{
				_logger.LogWarning( "InventoryPacketHandler _onRecv Dictionary Not Found id {id.ToString()}"  );
			}
		}
		private async ValueTask HandleC_InventoryRequestAsync(IClientSession session, ArraySegment<byte> buffer)
		{
			var packet = new C_InventoryRequest();
			packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
			await HandleC_InventoryRequestAsync(session, packet);
		}
		private async ValueTask HandleC_UseItemAsync(IClientSession session, ArraySegment<byte> buffer)
		{
			var packet = new C_UseItem();
			packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
			await HandleC_UseItemAsync(session, packet);
		}
		private async ValueTask HandleC_EquipItemAsync(IClientSession session, ArraySegment<byte> buffer)
		{
			var packet = new C_EquipItem();
			packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
			await HandleC_EquipItemAsync(session, packet);
		}
		private async ValueTask HandleC_UnequipItemAsync(IClientSession session, ArraySegment<byte> buffer)
		{
			var packet = new C_UnequipItem();
			packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
			await HandleC_UnequipItemAsync(session, packet);
		}
	}
}
