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
			_logger.LogInformation( "[Client] S_EnterGame - PlayerId: {PlayerId}, " +
				"PlayerName: {PlayerName}, Level: {Level}, HP: {HP}/{MaxHP}",
				packet.Player.PlayerId, packet.Player.Name,
				packet.Player.Level, packet.Player.CurrentHP, packet.Player.MaxHP );
			return ValueTask.CompletedTask;
		}

        public override ValueTask On_S_LeaveGame(Session session, S_LeaveGame packet)
        {
            _logger.LogInformation("[Client] Received S_LeaveGame. PlayerId: {PlayerId}", packet.PlayerId);
            return ValueTask.CompletedTask;
        }

        public override ValueTask On_S_Spawn(Session session, S_Spawn packet)
        {
			_logger.LogInformation( "[Client] S_Spawn - {PlayersCount} players spawned", packet.Players.Count );
			foreach(var player in packet.Players)
			{
				_logger.LogDebug( "  Player {PlayerId} at ({PosX:F2}, {PosY:F2}, {PosZ:F2})",
					player.PlayerId, player.PosInfo.PosX, player.PosInfo.PosY, player.PosInfo.PosZ );
			}
			return ValueTask.CompletedTask;
		}

        public override ValueTask On_S_Despawn(Session session, S_Despawn packet)
        {
            _logger.LogInformation("[Client] Received S_Despawn. ObjectIds: {ObjectIds}", string.Join(", ", packet.ObjectIds));
            return ValueTask.CompletedTask;
        }

        public override ValueTask On_S_Move(Session session, S_Move packet)
        {
			_logger.LogInformation( "[Client] S_Move - PlayerId: {PlayerId}, " +
				"3D Position: ({PosX:F2}, {PosY:F2}, {PosZ:F2}), " +
				"Rotation: ({RotX:F1}¡Æ, {RotY:F1}¡Æ, {RotZ:F1}¡Æ), " +
				"Timestamp: {Timestamp}",
				packet.PlayerId,
				packet.PosInfo.PosX, packet.PosInfo.PosY, packet.PosInfo.PosZ,
				packet.PosInfo.RotationX, packet.PosInfo.RotationY, packet.PosInfo.RotationZ,
				packet.PosInfo.Timestamp );
			return ValueTask.CompletedTask;
		}

        public override ValueTask On_S_Chat(Session session, S_Chat packet)
        {
            _logger.LogInformation("[Client] Received S_Chat. PlayerId: {PlayerId}, Message: {Message}", packet.PlayerId, packet.Message);
            return ValueTask.CompletedTask;
        }
    }
}