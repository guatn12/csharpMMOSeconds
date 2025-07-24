using Google.Protobuf;
using ServerCore;
using System;
using System.Collections.Concurrent;
using System.Net;

namespace Server
{
    public class GameSession : Session, IJobOwner
    {
        public int SessionId { get; private set; }
        public ConcurrentQueue<IJob> JobQueue { get; } = new ConcurrentQueue<IJob>();

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

            // JobQueue에 작업을 넣기 전 작업 개수
            int prevJobCount = JobQueue.Count;

            JobQueue.Enqueue( new Job( () =>
            {
                Program.PacketManagerInstance.HandlePacket( this, buffer );
            } ) );

			// 큐에 작업을 넣은 후, 만약 큐가 비어있다가(0개) 처음으로 작업이 추가된(1개) 상황이라면
			// JobQueueManager에게 "이 세션에서 처리할 작업이 생겼다"고 알려줍니다.
			if(prevJobCount == 0)
            {
                JobQueueManager.Instance.Push( this );
            }
        }

        public override void OnSend(int bytes)
        {
            LogManager.Debug("Packet Sent. SessionId: {SessionId}, Size: {Size}", this.SessionId, bytes);
        }
    }
}
