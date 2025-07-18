// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다.
// Target: Client

using ServerCore;
using System;
using System.Collections.Generic;
using Google.Protobuf;

public partial class PacketManager
{
	public static PacketManager Instance { get; } = new PacketManager();
	Dictionary<ushort, Action<PacketSession, ArraySegment<byte>>> _onRecv = new Dictionary<ushort, Action<PacketSession, ArraySegment<byte>>>();
	Dictionary<ushort, Func<IMessage, ArraySegment<byte>>> _makePacket = new Dictionary<ushort, Func<IMessage, ArraySegment<byte>>>();

	PacketManager() { Register(); }
	// 핸들러 자동 등록
	public void Register()
	{
		_makePacket.Add((ushort)PacketID.S_ENTER_GAME, MakeSendPacket);
		_onRecv.Add((ushort)PacketID.S_ENTER_GAME, (s, b) => HandlePacket<Protocol.S_ENTER_GAME>(s, b, On_S_ENTER_GAME));
		_makePacket.Add((ushort)PacketID.S_LEAVE_GAME, MakeSendPacket);
		_onRecv.Add((ushort)PacketID.S_LEAVE_GAME, (s, b) => HandlePacket<Protocol.S_LEAVE_GAME>(s, b, On_S_LEAVE_GAME));
		_makePacket.Add((ushort)PacketID.S_SPAWN, MakeSendPacket);
		_onRecv.Add((ushort)PacketID.S_SPAWN, (s, b) => HandlePacket<Protocol.S_SPAWN>(s, b, On_S_SPAWN));
		_makePacket.Add((ushort)PacketID.S_DESPAWN, MakeSendPacket);
		_onRecv.Add((ushort)PacketID.S_DESPAWN, (s, b) => HandlePacket<Protocol.S_DESPAWN>(s, b, On_S_DESPAWN));
		_makePacket.Add((ushort)PacketID.C_MOVE, MakeSendPacket);
		_makePacket.Add((ushort)PacketID.S_MOVE, MakeSendPacket);
		_onRecv.Add((ushort)PacketID.S_MOVE, (s, b) => HandlePacket<Protocol.S_MOVE>(s, b, On_S_MOVE));
		_makePacket.Add((ushort)PacketID.C_CHAT, MakeSendPacket);
		_makePacket.Add((ushort)PacketID.S_CHAT, MakeSendPacket);
		_onRecv.Add((ushort)PacketID.S_CHAT, (s, b) => HandlePacket<Protocol.S_CHAT>(s, b, On_S_CHAT));
	}

	 // 패킷 처리 로직
	void HandlePacket<T>(PacketSession session, ArraySegment<byte> buffer, Action<PacketSession, T> handler) where T : Imessage, new()
	{
		T pkt = new T();
		pkt.MergeFrom(buffer);
		handler.Invoke(session, pkt);
	}

	// 신규 패킷 생성 로직
	ArraySegment<byte> MakeSendPacket(IMessage Packet)
	{
		ushort packetId = (ushort)Enum.Parse<PacketID>(packet.Descriptor.Name);
		ushort size = (ushort)packet.CalculateSize();
		byte[] buffer = new byte[size+4];
		Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, buffer, 0, sizeof(ushort));
		Array.Copy(BitConverter.GetBytes(packetId), 0, buffer, 2, sizeof(ushort));
		packet.WriteTo(new System.IO.MemoryStream(buffer, 4, size));
		return new ArraySegment<byte>(buffer);
	}

	 // partial 함수 선언부
	partial void On_S_ENTER_GAME(PacketSession session, Protocol.S_ENTER_GAME packet);
	partial void On_S_LEAVE_GAME(PacketSession session, Protocol.S_LEAVE_GAME packet);
	partial void On_S_SPAWN(PacketSession session, Protocol.S_SPAWN packet);
	partial void On_S_DESPAWN(PacketSession session, Protocol.S_DESPAWN packet);
	partial void On_S_MOVE(PacketSession session, Protocol.S_MOVE packet);
	partial void On_S_CHAT(PacketSession session, Protocol.S_CHAT packet);
}
