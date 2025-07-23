// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다.
// Target: Server

using ServerCore;
using System;
using System.Collections.Generic;
using Google.Protobuf;
using Protocol;

public class PacketManager
{
	IPacketHandler _handler;
	Dictionary<ushort, Action<Session, ArraySegment<byte>>> _onRecv = new Dictionary<ushort, Action<Session, ArraySegment<byte>>>();
	Dictionary<ushort, Func<IMessage, ArraySegment<byte>>> _makePacket = new Dictionary<ushort, Func<IMessage, ArraySegment<byte>>>();
	Dictionary<Type, PacketID> _packetTypeToId = new Dictionary<Type, PacketID>();

	public PacketManager( IPacketHandler handler )
	{
		_handler = handler;
		Register();
	}

	// 핸들러 자동 등록
	public void Register()
	{
		_makePacket.Add((ushort)PacketID.S_EnterGame, MakeSendPacket);
		_packetTypeToId.Add( typeof( Protocol.S_EnterGame ), PacketID.S_EnterGame );
		_makePacket.Add((ushort)PacketID.S_LeaveGame, MakeSendPacket);
		_packetTypeToId.Add( typeof( Protocol.S_LeaveGame ), PacketID.S_LeaveGame );
		_makePacket.Add((ushort)PacketID.S_Spawn, MakeSendPacket);
		_packetTypeToId.Add( typeof( Protocol.S_Spawn ), PacketID.S_Spawn );
		_makePacket.Add((ushort)PacketID.S_Despawn, MakeSendPacket);
		_packetTypeToId.Add( typeof( Protocol.S_Despawn ), PacketID.S_Despawn );
		_onRecv.Add( (ushort)PacketID.C_Move, ( s, b ) => HandlePacket<Protocol.C_Move>( s, b, _handler.MovementPacketHandler.On_C_Move ) );
		_makePacket.Add((ushort)PacketID.S_Move, MakeSendPacket);
		_packetTypeToId.Add( typeof( Protocol.S_Move ), PacketID.S_Move );
		_onRecv.Add( (ushort)PacketID.C_Chat, ( s, b ) => HandlePacket<Protocol.C_Chat>( s, b, _handler.ChatPacketHandler.On_C_Chat ) );
		_makePacket.Add((ushort)PacketID.S_Chat, MakeSendPacket);
		_packetTypeToId.Add( typeof( Protocol.S_Chat ), PacketID.S_Chat );
	}

	// 패킷 진입 처리점
	public void HandlePacket( Session session, ArraySegment<byte> buffer )
	{
		ushort count = 0;
		ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
		count += 2;
		ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);
		count += 2;

		if(_onRecv.TryGetValue( id, out var action ))
		{
			action.Invoke( session, new ArraySegment<byte>( buffer.Array, buffer.Offset + count, size - count ) );
		}
	}

	 // 패킷 처리 로직
	private void HandlePacket<T>(Session session, ArraySegment<byte> buffer, Action<Session, T> handler) where T : IMessage, new()
	{
		T pkt = new T();
		pkt.MergeFrom(buffer);
		handler.Invoke(session, pkt);
	}

	// 신규 패킷 생성 로직
	public ArraySegment<byte> MakeSendPacket(IMessage Packet)
	{
		PacketID packetId;
		bool getValue = _packetTypeToId.TryGetValue( Packet.GetType(), out packetId );
		if (!getValue)
			return new ArraySegment<byte>();
		ushort size = (ushort)Packet.CalculateSize();
		byte[] buffer = new byte[size+4];
		Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, buffer, 0, sizeof(ushort));
		Array.Copy(BitConverter.GetBytes((ushort)packetId), 0, buffer, 2, sizeof(ushort));
		Packet.WriteTo(new System.IO.MemoryStream(buffer, 4, size));
		return new ArraySegment<byte>(buffer);
	}

}
