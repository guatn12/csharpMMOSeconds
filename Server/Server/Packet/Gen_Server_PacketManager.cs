// [자동 생성] 새로운 제네릭 Job 시스템용 PacketManager
// Target: Server

using Google.Protobuf;
using Protocol;
using Microsoft.Extensions.Logging;
using Server.Jobs;
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
            _packetTypeToId.Add(typeof(S_EnterGame), PacketID.S_EnterGame);
            _packetTypeToId.Add(typeof(S_LeaveGame), PacketID.S_LeaveGame);
            _packetTypeToId.Add(typeof(S_Spawn), PacketID.S_Spawn);
            _packetTypeToId.Add(typeof(S_Despawn), PacketID.S_Despawn);
            _packetTypeToId.Add(typeof(S_Move), PacketID.S_Move);
            _packetTypeToId.Add(typeof(S_Chat), PacketID.S_Chat);
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
