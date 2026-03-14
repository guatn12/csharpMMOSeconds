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
        public virtual ValueTask On_S_EnterGame(NetworkSession session, S_EnterGame packet) { Console.WriteLine("Received but not handled: S_EnterGame"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_LeaveGame(NetworkSession session, S_LeaveGame packet) { Console.WriteLine("Received but not handled: S_LeaveGame"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Spawn(NetworkSession session, S_Spawn packet) { Console.WriteLine("Received but not handled: S_Spawn"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Despawn(NetworkSession session, S_Despawn packet) { Console.WriteLine("Received but not handled: S_Despawn"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Move(NetworkSession session, S_Move packet) { Console.WriteLine("Received but not handled: S_Move"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Chat(NetworkSession session, S_Chat packet) { Console.WriteLine("Received but not handled: S_Chat"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_PlayerUpdate(NetworkSession session, S_PlayerUpdate packet) { Console.WriteLine("Received but not handled: S_PlayerUpdate"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_PlayerStat(NetworkSession session, S_PlayerStat packet) { Console.WriteLine("Received but not handled: S_PlayerStat"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Damage(NetworkSession session, S_Damage packet) { Console.WriteLine("Received but not handled: S_Damage"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Heal(NetworkSession session, S_Heal packet) { Console.WriteLine("Received but not handled: S_Heal"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_LevelUp(NetworkSession session, S_LevelUp packet) { Console.WriteLine("Received but not handled: S_LevelUp"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_InventoryData(NetworkSession session, S_InventoryData packet) { Console.WriteLine("Received but not handled: S_InventoryData"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_UseItem(NetworkSession session, S_UseItem packet) { Console.WriteLine("Received but not handled: S_UseItem"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_ItemEquipped(NetworkSession session, S_ItemEquipped packet) { Console.WriteLine("Received but not handled: S_ItemEquipped"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_ItemUnequipped(NetworkSession session, S_ItemUnequipped packet) { Console.WriteLine("Received but not handled: S_ItemUnequipped"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_ItemAdded(NetworkSession session, S_ItemAdded packet) { Console.WriteLine("Received but not handled: S_ItemAdded"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_InventoryUpdate(NetworkSession session, S_InventoryUpdate packet) { Console.WriteLine("Received but not handled: S_InventoryUpdate"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_MonsterDie(NetworkSession session, S_MonsterDie packet) { Console.WriteLine("Received but not handled: S_MonsterDie"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_MonsterUpdate(NetworkSession session, S_MonsterUpdate packet) { Console.WriteLine("Received but not handled: S_MonsterUpdate"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_ChangeRoom(NetworkSession session, S_ChangeRoom packet) { Console.WriteLine("Received but not handled: S_ChangeRoom"); return ValueTask.CompletedTask; }
        public virtual ValueTask On_S_Pong(NetworkSession session, S_Pong packet) { Console.WriteLine("Received but not handled: S_Pong"); return ValueTask.CompletedTask; }
    }

    public class PacketManager
    {
        private readonly ILogger<PacketManager> _logger;
        private readonly BaseClientPacketHandler _handler;
        private readonly Dictionary<ushort, Func<NetworkSession, ArraySegment<byte>, ValueTask>> _onRecv;
        private readonly Dictionary<Type, PacketID> _packetTypeToId;

        public PacketManager(ILogger<PacketManager> logger, BaseClientPacketHandler handler)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _onRecv = new Dictionary<ushort, Func<NetworkSession, ArraySegment<byte>, ValueTask>>();
            _packetTypeToId = new Dictionary<Type, PacketID>();
            Register();
        }

        private void Register()
        {
            _packetTypeToId.Add(typeof(Protocol.C_EnterGame), PacketID.C_EnterGame);
            _onRecv.Add((ushort)PacketID.S_EnterGame, HandlePacket<S_EnterGame>(_handler.On_S_EnterGame));
            _onRecv.Add((ushort)PacketID.S_LeaveGame, HandlePacket<S_LeaveGame>(_handler.On_S_LeaveGame));
            _onRecv.Add((ushort)PacketID.S_Spawn, HandlePacket<S_Spawn>(_handler.On_S_Spawn));
            _onRecv.Add((ushort)PacketID.S_Despawn, HandlePacket<S_Despawn>(_handler.On_S_Despawn));
            _packetTypeToId.Add(typeof(Protocol.C_Move), PacketID.C_Move);
            _onRecv.Add((ushort)PacketID.S_Move, HandlePacket<S_Move>(_handler.On_S_Move));
            _packetTypeToId.Add(typeof(Protocol.C_Chat), PacketID.C_Chat);
            _onRecv.Add((ushort)PacketID.S_Chat, HandlePacket<S_Chat>(_handler.On_S_Chat));
            _onRecv.Add((ushort)PacketID.S_PlayerUpdate, HandlePacket<S_PlayerUpdate>(_handler.On_S_PlayerUpdate));
            _onRecv.Add((ushort)PacketID.S_PlayerStat, HandlePacket<S_PlayerStat>(_handler.On_S_PlayerStat));
            _packetTypeToId.Add(typeof(Protocol.C_PlayerInfo), PacketID.C_PlayerInfo);
            _packetTypeToId.Add(typeof(Protocol.C_UseSkill), PacketID.C_UseSkill);
            _onRecv.Add((ushort)PacketID.S_Damage, HandlePacket<S_Damage>(_handler.On_S_Damage));
            _onRecv.Add((ushort)PacketID.S_Heal, HandlePacket<S_Heal>(_handler.On_S_Heal));
            _onRecv.Add((ushort)PacketID.S_LevelUp, HandlePacket<S_LevelUp>(_handler.On_S_LevelUp));
            _packetTypeToId.Add(typeof(Protocol.C_InventoryRequest), PacketID.C_InventoryRequest);
            _onRecv.Add((ushort)PacketID.S_InventoryData, HandlePacket<S_InventoryData>(_handler.On_S_InventoryData));
            _packetTypeToId.Add(typeof(Protocol.C_UseItem), PacketID.C_UseItem);
            _onRecv.Add((ushort)PacketID.S_UseItem, HandlePacket<S_UseItem>(_handler.On_S_UseItem));
            _packetTypeToId.Add(typeof(Protocol.C_EquipItem), PacketID.C_EquipItem);
            _onRecv.Add((ushort)PacketID.S_ItemEquipped, HandlePacket<S_ItemEquipped>(_handler.On_S_ItemEquipped));
            _packetTypeToId.Add(typeof(Protocol.C_UnequipItem), PacketID.C_UnequipItem);
            _onRecv.Add((ushort)PacketID.S_ItemUnequipped, HandlePacket<S_ItemUnequipped>(_handler.On_S_ItemUnequipped));
            _onRecv.Add((ushort)PacketID.S_ItemAdded, HandlePacket<S_ItemAdded>(_handler.On_S_ItemAdded));
            _onRecv.Add((ushort)PacketID.S_InventoryUpdate, HandlePacket<S_InventoryUpdate>(_handler.On_S_InventoryUpdate));
            _onRecv.Add((ushort)PacketID.S_MonsterDie, HandlePacket<S_MonsterDie>(_handler.On_S_MonsterDie));
            _onRecv.Add((ushort)PacketID.S_MonsterUpdate, HandlePacket<S_MonsterUpdate>(_handler.On_S_MonsterUpdate));
            _packetTypeToId.Add(typeof(Protocol.C_ChangeRoom), PacketID.C_ChangeRoom);
            _onRecv.Add((ushort)PacketID.S_ChangeRoom, HandlePacket<S_ChangeRoom>(_handler.On_S_ChangeRoom));
            _packetTypeToId.Add(typeof(Protocol.C_Ping), PacketID.C_Ping);
            _onRecv.Add((ushort)PacketID.S_Pong, HandlePacket<S_Pong>(_handler.On_S_Pong));
        }

        public async ValueTask HandlePacket(NetworkSession session, ArraySegment<byte> buffer)
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

        private Func<NetworkSession, ArraySegment<byte>, ValueTask> HandlePacket<T>(Func<NetworkSession, T, ValueTask> handler) where T : IMessage, new()
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
