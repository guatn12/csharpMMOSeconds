using Google.Protobuf;
using ServerCore;
using System;
using System.Net;

namespace Server
{
    public class GameSession : Session
    {
        public int SessionId { get; private set; }

        public void Send(IMessage packet)
        {
            ArraySegment<byte> segment = Program.PacketManagerInstance.MakeSendPacket(packet);
            base.Send(segment);
        }

        public override void OnConnected(EndPoint endPoint)
        {
            this.SessionId = GetHashCode(); // 임시 세션 ID 발급
            LogManager.Info("Client Connected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint);
            // TODO : 클라이언트에게 입장 패킷 전송.
        }

        public override void OnDisConnected(EndPoint endPoint)
        {
            LogManager.Info("Client Disconnected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint);
        }

        public override void OnRecvPacket(ArraySegment<byte> buffer)
        {
            ushort packetIdValue = BitConverter.ToUInt16(buffer.Array, buffer.Offset + 2);
            PacketID packetId = (PacketID)packetIdValue;

            LogManager.Debug("Packet Received. SessionId: {SessionId}, PacketID: {PacketID}, Size: {Size}",
                this.SessionId, packetId, buffer.Count);

            JobPriority priority;
            switch (packetId)
            {
                case PacketID.C_Move:
                    priority = JobPriority.High;
                    break;
                case PacketID.C_Chat:
                    priority = JobPriority.Medium;
                    break;
                default:
                    priority = JobPriority.Low;
                    break;
            }

            Action jobAction = () =>
            {
                Program.PacketManagerInstance.HandlePacket(this, buffer);
            };

            GameJob gameJob = new GameJob(jobAction, priority);
            JobQueueManager.Instance.Enqueue(gameJob);
        }

        public override void OnSend(int bytes)
        {
            LogManager.Debug("Packet Sent. SessionId: {SessionId}, Size: {Size}", this.SessionId, bytes);
        }
    }
}
