// [자동 생성] 새로운 제네릭 Job 시스템용 PacketManager
// Target: Server

using Google.Protobuf;
using Protocol;
using Microsoft.Extensions.Logging;
using Server.Core.Jobs;
using Server.Core.Session;
using Server.Room;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Server.Packet
{
    public class PacketManager
    {
        private readonly JobPool _jobPool;
        private readonly JobQueueManager _jobQueueManager;
        private readonly ILogger<PacketManager> _logger;
        private readonly Dictionary<ushort, Func<GameSession, ArraySegment<byte>, ValueTask>> _onRecv;
        private static readonly Dictionary<Type, Func<GameSession, IRoom, IMessage, ILogger, ValueTask>> _packetLogicMap;
        private readonly Dictionary<Type, PacketID> _packetTypeToId;

        static PacketManager()
        {
            _packetLogicMap = new Dictionary<Type, Func<GameSession, IRoom, IMessage, ILogger, ValueTask>>
            {
                [typeof(C_Move)] = async (session, room, packet, logger) =>
                    await (room?.HandlePlayerMoveAsync(session, (C_Move)packet, logger) ?? Task.CompletedTask),
                [typeof(C_Chat)] = async (session, room, packet, logger) =>
                    await (room?.HandlePlayerChatAsync(session, (C_Chat)packet, logger) ?? Task.CompletedTask),
                [typeof(C_PlayerInfo)] = async (session, room, packet, logger) =>
                    await (room?.HandlePlayerPlayerInfoAsync(session, (C_PlayerInfo)packet, logger) ?? Task.CompletedTask),
                [typeof(C_UseSkill)] = async (session, room, packet, logger) =>
                    await (room?.HandlePlayerUseSkillAsync(session, (C_UseSkill)packet, logger) ?? Task.CompletedTask),
                [typeof(C_InventoryRequest)] = async (session, room, packet, logger) =>
                    await (room?.HandlePlayerInventoryRequestAsync(session, (C_InventoryRequest)packet, logger) ?? Task.CompletedTask),
                [typeof(C_UseItem)] = async (session, room, packet, logger) =>
                    await (room?.HandlePlayerUseItemAsync(session, (C_UseItem)packet, logger) ?? Task.CompletedTask),
                [typeof(C_EquipItem)] = async (session, room, packet, logger) =>
                    await (room?.HandlePlayerEquipItemAsync(session, (C_EquipItem)packet, logger) ?? Task.CompletedTask),
                [typeof(C_UnequipItem)] = async (session, room, packet, logger) =>
                    await (room?.HandlePlayerUnequipItemAsync(session, (C_UnequipItem)packet, logger) ?? Task.CompletedTask),
            };
        }

        public PacketManager(JobPool jobPool, JobQueueManager jobQueueManager, ILogger<PacketManager> logger)
        {
            _jobPool = jobPool ?? throw new ArgumentNullException(nameof(jobPool));
            _jobQueueManager = jobQueueManager ?? throw new ArgumentNullException(nameof(jobQueueManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _onRecv = new Dictionary<ushort, Func<GameSession, ArraySegment<byte>, ValueTask>>();
            _packetTypeToId = new Dictionary<Type, PacketID>();
            Register();
        }

        private void Register()
        {
            _onRecv.Add((ushort)PacketID.C_Move, HandleC_MoveAsync);
            _onRecv.Add((ushort)PacketID.C_Chat, HandleC_ChatAsync);
            _onRecv.Add((ushort)PacketID.C_PlayerInfo, HandleC_PlayerInfoAsync);
            _onRecv.Add((ushort)PacketID.C_UseSkill, HandleC_UseSkillAsync);
            _onRecv.Add((ushort)PacketID.C_InventoryRequest, HandleC_InventoryRequestAsync);
            _onRecv.Add((ushort)PacketID.C_UseItem, HandleC_UseItemAsync);
            _onRecv.Add((ushort)PacketID.C_EquipItem, HandleC_EquipItemAsync);
            _onRecv.Add((ushort)PacketID.C_UnequipItem, HandleC_UnequipItemAsync);
            _packetTypeToId.Add(typeof(S_EnterGame), PacketID.S_EnterGame);
            _packetTypeToId.Add(typeof(S_LeaveGame), PacketID.S_LeaveGame);
            _packetTypeToId.Add(typeof(S_Spawn), PacketID.S_Spawn);
            _packetTypeToId.Add(typeof(S_Despawn), PacketID.S_Despawn);
            _packetTypeToId.Add(typeof(S_Move), PacketID.S_Move);
            _packetTypeToId.Add(typeof(S_Chat), PacketID.S_Chat);
            _packetTypeToId.Add(typeof(S_PlayerUpdate), PacketID.S_PlayerUpdate);
            _packetTypeToId.Add(typeof(S_PlayerStat), PacketID.S_PlayerStat);
            _packetTypeToId.Add(typeof(S_Damage), PacketID.S_Damage);
            _packetTypeToId.Add(typeof(S_Heal), PacketID.S_Heal);
            _packetTypeToId.Add(typeof(S_LevelUp), PacketID.S_LevelUp);
            _packetTypeToId.Add(typeof(S_InventoryData), PacketID.S_InventoryData);
            _packetTypeToId.Add(typeof(S_UseItem), PacketID.S_UseItem);
            _packetTypeToId.Add(typeof(S_ItemEquipped), PacketID.S_ItemEquipped);
            _packetTypeToId.Add(typeof(S_ItemUnequipped), PacketID.S_ItemUnequipped);
            _packetTypeToId.Add(typeof(S_ItemAdded), PacketID.S_ItemAdded);
            _packetTypeToId.Add(typeof(S_InventoryUpdate), PacketID.S_InventoryUpdate);
        }

        private async ValueTask HandleC_MoveAsync(GameSession session, ArraySegment<byte> buffer)
        {
            var packet = new C_Move();
            packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
            await HandlePacketLogic<C_Move>(session, packet);
        }

        private async ValueTask HandleC_ChatAsync(GameSession session, ArraySegment<byte> buffer)
        {
            var packet = new C_Chat();
            packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
            await HandlePacketLogic<C_Chat>(session, packet);
        }

        private async ValueTask HandleC_PlayerInfoAsync(GameSession session, ArraySegment<byte> buffer)
        {
            var packet = new C_PlayerInfo();
            packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
            await HandlePacketLogic<C_PlayerInfo>(session, packet);
        }

        private async ValueTask HandleC_UseSkillAsync(GameSession session, ArraySegment<byte> buffer)
        {
            var packet = new C_UseSkill();
            packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
            await HandlePacketLogic<C_UseSkill>(session, packet);
        }

        private async ValueTask HandleC_InventoryRequestAsync(GameSession session, ArraySegment<byte> buffer)
        {
            var packet = new C_InventoryRequest();
            packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
            await HandlePacketLogic<C_InventoryRequest>(session, packet);
        }

        private async ValueTask HandleC_UseItemAsync(GameSession session, ArraySegment<byte> buffer)
        {
            var packet = new C_UseItem();
            packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
            await HandlePacketLogic<C_UseItem>(session, packet);
        }

        private async ValueTask HandleC_EquipItemAsync(GameSession session, ArraySegment<byte> buffer)
        {
            var packet = new C_EquipItem();
            packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
            await HandlePacketLogic<C_EquipItem>(session, packet);
        }

        private async ValueTask HandleC_UnequipItemAsync(GameSession session, ArraySegment<byte> buffer)
        {
            var packet = new C_UnequipItem();
            packet.MergeFrom(buffer.Array, buffer.Offset, buffer.Count);
            await HandlePacketLogic<C_UnequipItem>(session, packet);
        }

        private async ValueTask HandlePacketLogic<T>(GameSession session, T packet) where T : IMessage
        {
            try
            {
                var room = session.CurrentRoom;
                
                // 핸들러 검색
                if (!_packetLogicMap.TryGetValue(typeof(T), out var handler))
                {
                    _logger.LogWarning("No handler found for packet type: {PacketType}", typeof(T).Name);
                    return;
                }

                // PacketJob 생성 및 설정
                var job = _jobPool.Get<PacketJob<T>>();
                job.Initialize(session, room, packet, _logger);
                job.SetHandler(handler);

                // Job Queue에 추가
                await _jobQueueManager.PushAsync(job);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling packet {PacketType} from session {SessionId}",
                    typeof(T).Name, session.SessionId);
            }
        }

        public async ValueTask HandlePacket(GameSession session, ArraySegment<byte> buffer)
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
                _logger.LogWarning("Unknown packet ID: {PacketId} from session {SessionId}", id, session.SessionId);
            }
        }

        // 패킷 직렬화 및 전송용 버퍼 생성
        public ArraySegment<byte> MakeSendPacket(IMessage packet)
        {
            PacketID packetId;
            bool getValue = _packetTypeToId.TryGetValue(packet.GetType(), out packetId);
            if (!getValue)
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
