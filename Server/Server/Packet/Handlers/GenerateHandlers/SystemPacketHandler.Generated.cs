// [자동 생성] SystemPacketHandler Dictionary 초기화
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
	public partial class SystemPacketHandler : IPacketHandler
	{
		/// <summary>
		/// 테스트 및 디버깅용 Dictionary.
		/// 역직렬화된 IMessage 객체를 직접 처리할 때 사용.
		/// 런타임 패킷 처리는 _onRecv Dictionary 사용.
		/// </summary>
		public Dictionary<Type, Func<GameSession, IMessage, Task>> Handlers { get; private set; }
		private Dictionary<ushort, Func<GameSession, ArraySegment<byte>, ValueTask>> _onRecv;

		private void InitializeHandlers()
		{
			Handlers = new Dictionary<Type, Func<GameSession, IMessage, Task>>();
			_onRecv = new Dictionary<ushort, Func<GameSession, ArraySegment<byte>, ValueTask>>();

			Handlers.Add( typeof( C_EnterGame ), async ( s, p ) => await HandleC_EnterGameAsync( s, (C_EnterGame)p ) );
			_onRecv.Add( (ushort)PacketID.C_EnterGame, HandleC_EnterGameAsync );
		}

		public async ValueTask HandleAsync( GameSession session, ushort id, ArraySegment<byte> buffer )
		{
			if(_onRecv.TryGetValue( id, out var handler ))
			{
				await handler( session, buffer );
			}
			else
			{
				_logger.LogWarning( "SystemPacketHandler _onRecv Dictionary Not Found id {id.ToString()}" );
			}
		}
		private async ValueTask HandleC_EnterGameAsync( GameSession session, ArraySegment<byte> buffer )
		{
			var packet = new C_EnterGame();
			packet.MergeFrom( buffer.Array, buffer.Offset, buffer.Count );
			await HandleC_EnterGameAsync( session, packet );
		}

	}
}
