// [자동 생성] Protocol.proto 파일을 기반으로 자동 생성된 코드입니다.
// Target: Client (신규 구조 - Inheritance Model)

using ServerCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf;
using Protocol;
using Microsoft.Extensions.Logging;

namespace DummyClient.Packet
{
    public abstract class BaseClientPacketHandler
    {
        public virtual ValueTask On_S_EnterGame(Session session, S_EnterGame packet) { Console.WriteLine("Received but not handled: S_EnterGame"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_LeaveGame(Session session, S_LeaveGame packet) { Console.WriteLine("Received but not handled: S_LeaveGame"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Spawn(Session session, S_Spawn packet) { Console.WriteLine("Received but not handled: S_Spawn"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Despawn(Session session, S_Despawn packet) { Console.WriteLine("Received but not handled: S_Despawn"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Move(Session session, S_Move packet) { Console.WriteLine("Received but not handled: S_Move"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Chat(Session session, S_Chat packet) { Console.WriteLine("Received but not handled: S_Chat"); return ValueTask.CompletedTask; }
    }

    public class PacketManager
    {
        private readonly ILogger<PacketManager> _logger;
        private readonly BaseClientPacketHandler _handler;
        private readonly Dictionary<ushort, Func<Session, ArraySegment<byte>, ValueTask>> _onRecv;
        private readonly Dictionary<Type, PacketID> _packetTypeToId;

        public PacketManager(ILogger<PacketManager> logger, BaseClientPacketHandler handler)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _onRecv = new Dictionary<ushort, Func<Session, ArraySegment<byte>, ValueTask>>();
            _packetTypeToId = new Dictionary<Type, PacketID>();
            Register();
        }

        private void Register()
        {
            _onRecv.Add((ushort)PacketID.S_EnterGame, HandlePacket<S_EnterGame>(_handler.On_S_EnterGame));
            _onRecv.Add((ushort)PacketID.S_LeaveGame, HandlePacket<S_LeaveGame>(_handler.On_S_LeaveGame));
            _onRecv.Add((ushort)PacketID.S_Spawn, HandlePacket<S_Spawn>(_handler.On_S_Spawn));
            _onRecv.Add((ushort)PacketID.S_Despawn, HandlePacket<S_Despawn>(_handler.On_S_Despawn));
            _packetTypeToId.Add(typeof(Protocol.C_Move), PacketID.C_Move);
            _onRecv.Add((ushort)PacketID.S_Move, HandlePacket<S_Move>(_handler.On_S_Move));
            _packetTypeToId.Add(typeof(Protocol.C_Chat), PacketID.C_Chat);
            _onRecv.Add((ushort)PacketID.S_Chat, HandlePacket<S_Chat>(_handler.On_S_Chat));
        }

        public async ValueTask HandlePacket(Session session, ArraySegment<byte> buffer)
        {
            ushort count = 0;
            ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
            count += 2;
            ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);
            count += 2;

            if (_onRecv.TryGetValue(id, out var handler))
            {
                await handler(session, new ArraySegment<byte>(buffer.Array, buffer.Offset + count, size - count));
            }
            else
            {
                _logger.LogWarning("Unknown packet ID: {PacketId}", id);
            }
        }

        private Func<Session, ArraySegment<byte>, ValueTask> HandlePacket<T>(Func<Session, T, ValueTask> handler) where T : IMessage, new()
        {
            return async (session, buffer) =>
            {
                try
                {
                    var packet = new T();
                    packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
                    await handler(session, packet);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling packet {PacketType}", typeof(T).Name);
                }
            };
        }

        public ArraySegment<byte> MakeSendPacket(IMessage packet)
        {
            if (!_packetTypeToId.TryGetValue(packet.GetType(), out var packetId))
            {
                _logger.LogWarning("Unknown packet type for MakeSendPacket: {PacketType}", packet.GetType().Name);
                return new ArraySegment<byte>();
            }

            ushort size = (ushort)packet.CalculateSize();
            byte[] buffer = new byte[size + 4];
            Array.Copy(BitConverter.GetBytes((ushort)(size + 4)), 0, buffer, 0, sizeof(ushort));
            Array.Copy(BitConverter.GetBytes((ushort)packetId), 0, buffer, 2, sizeof(ushort));
            packet.WriteTo(new System.IO.MemoryStream(buffer, 4, size));
            return new ArraySegment<byte>(buffer);
        }
    }
}
