using DummyClient.Packet;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Protocol; // Added for packet types
using Microsoft.Extensions.Logging; // Added for ILogger

namespace DummyClient.Packet
{
    public class ClientPacketHandler : BaseClientPacketHandler
    {
        private readonly ILogger<ClientPacketHandler> _logger;

        public ClientPacketHandler(ILogger<ClientPacketHandler> logger)
        {
            _logger = logger;
        }

        public override ValueTask On_S_EnterGame(Session session, S_EnterGame packet)
        {
            _logger.LogInformation("[Client] Received S_EnterGame. PlayerId: {PlayerId}", packet.Player.PlayerId);
            return ValueTask.CompletedTask;
        }

        public override ValueTask On_S_LeaveGame(Session session, S_LeaveGame packet)
        {
            _logger.LogInformation("[Client] Received S_LeaveGame. PlayerId: {PlayerId}", packet.PlayerId);
            return ValueTask.CompletedTask;
        }

        public override ValueTask On_S_Spawn(Session session, S_Spawn packet)
        {
            _logger.LogInformation("[Client] Received S_Spawn. Players: {PlayersCount}", packet.Players.Count);
            return ValueTask.CompletedTask;
        }

        public override ValueTask On_S_Despawn(Session session, S_Despawn packet)
        {
            _logger.LogInformation("[Client] Received S_Despawn. ObjectIds: {ObjectIds}", string.Join(", ", packet.ObjectIds));
            return ValueTask.CompletedTask;
        }

        public override ValueTask On_S_Move(Session session, S_Move packet)
        {
            _logger.LogInformation("[Client] Received S_Move. PlayerId: {PlayerId}, Pos: ({PosX}, {PosY}, {PosZ})", packet.PlayerId, packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ);
            return ValueTask.CompletedTask;
        }

        public override ValueTask On_S_Chat(Session session, S_Chat packet)
        {
            _logger.LogInformation("[Client] Received S_Chat. PlayerId: {PlayerId}, Message: {Message}", packet.PlayerId, packet.Message);
            return ValueTask.CompletedTask;
        }
    }
}