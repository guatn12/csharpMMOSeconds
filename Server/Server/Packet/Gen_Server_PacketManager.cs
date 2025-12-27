// [자동 생성] Category 핸들러 시스템용 PacketManager
// Target: Server

using Google.Protobuf;
using Protocol;
using Microsoft.Extensions.Logging;
using Server.Core.Session;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Server.Packet.Handlers;

namespace Server.Packet
{
	public class PacketManager
	{
		private readonly ILogger<PacketManager> _logger;
		private readonly Dictionary<Type, PacketID> _packetTypeToId;
		private readonly Dictionary<PacketID, PacketCategory> _packetCategoryCache = new();
		public PacketManager(ILogger<PacketManager> logger)
		{
			_logger = logger;
			_packetTypeToId = new Dictionary<Type, PacketID>();
			Register();
		}
        private void Register()
        {
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
            _packetTypeToId.Add(typeof(S_MonsterSpawn), PacketID.S_MonsterSpawn);
            _packetTypeToId.Add(typeof(S_MonsterDespawn), PacketID.S_MonsterDespawn);
            _packetTypeToId.Add(typeof(S_MonsterMove), PacketID.S_MonsterMove);
            _packetTypeToId.Add(typeof(S_MonsterAttack), PacketID.S_MonsterAttack);
            _packetTypeToId.Add(typeof(S_MonsterDie), PacketID.S_MonsterDie);
            _packetTypeToId.Add(typeof(S_MonsterUpdate), PacketID.S_MonsterUpdate);
            // System 카테고리
            _packetCategoryCache.Add(PacketID.C_EnterGame, PacketCategory.System);
            // Room 카테고리
            _packetCategoryCache.Add(PacketID.C_Move, PacketCategory.Room);
            _packetCategoryCache.Add(PacketID.C_Chat, PacketCategory.Room);
            _packetCategoryCache.Add(PacketID.C_PlayerInfo, PacketCategory.Room);
            _packetCategoryCache.Add(PacketID.C_UseSkill, PacketCategory.Room);
            // Inventory 카테고리
            _packetCategoryCache.Add(PacketID.C_InventoryRequest, PacketCategory.Inventory);
            _packetCategoryCache.Add(PacketID.C_UseItem, PacketCategory.Inventory);
            _packetCategoryCache.Add(PacketID.C_EquipItem, PacketCategory.Inventory);
            _packetCategoryCache.Add(PacketID.C_UnequipItem, PacketCategory.Inventory);
            // Combat 카테고리
            _packetCategoryCache.Add(PacketID.C_AttackMonster, PacketCategory.Combat);
        }

        public async ValueTask HandlePacket(GameSession session, ArraySegment<byte> buffer)
        {
            ushort count = 0;
            ushort size = BitConverter.ToUInt16(buffer.Array, buffer.Offset);
            count += 2;
            ushort id = BitConverter.ToUInt16(buffer.Array, buffer.Offset + count);
            count += 2;

			// CurrentRoom 확인
			var room = session.CurrentRoom;
			if(room == null)
			{
				_logger.LogWarning( "Player {PlayerId} not in any room for packet {PacketId}", session.PlayerId, id.ToString() );
				return;
			}

            PacketCategory packetCategory = GetPacketCategory((PacketID)id);
            IPacketHandler packetHandler = packetCategory switch
            {
                PacketCategory.System => room.SystemPacketHandler,
                PacketCategory.Inventory => room.InventoryPacketHandler,
                PacketCategory.Room => room.RoomPacketHandler,
                PacketCategory.Combat => room.CombatPacketHandler,
                _ => null
            };

			if(packetHandler != null)
			{
				await packetHandler.HandleAsync( session, id, buffer );
			}
			else
			{
				_logger.LogWarning( "No handler for category: {Category}", packetCategory );
			}
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

		private PacketCategory GetPacketCategory(PacketID id)
		{
			return _packetCategoryCache.TryGetValue(id, out var category) ? category : PacketCategory.NoneCategory;
		}
    }
}