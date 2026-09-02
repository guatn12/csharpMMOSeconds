using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Extensions;
using Server.Room;
using Server.Utils;
using System.Linq;
using System.Threading.Tasks;

namespace Server.Packet.Handlers
{
	public partial class InventoryPacketHandler
	{
		private readonly ILogger<InventoryPacketHandler> _logger;
		private readonly BaseRoom _room;

		public InventoryPacketHandler(ILogger<InventoryPacketHandler> logger, BaseRoom room )
		{
			_logger = logger;
			_room = room;
			InitializeHandlers();
		}

		/// <summary>
		/// C_InventoryRequest 패킷 처리
		/// 플레이어 인벤토리 정보 조회
		/// </summary>
		private async Task HandleC_InventoryRequestAsync( IClientSession session, C_InventoryRequest packet)
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "InventoryRequest validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 2. 응답
			var response = new S_InventoryData()
			{
				MaxSlots = session.Player.Inventory.MaxSlots,
				Gold = session.Player.Inventory.Gold,
			};

			var items = session.Player.Inventory.GetAllItems();
			if(items != null && 0 < items.Count)
			{
				var protoItems = items.Select(item => item.ToProto());
				response.Items.AddRange( protoItems );
			}

			_room.SendToPlayer( session, response );

			_logger.LogDebug( "Player {PlayerId} requested inventory data", session.PlayerId );
		}

		public async Task HandleC_EquipmentRequestAsync( IClientSession session, C_EquipmentRequest packet )
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "EquipmentRequest validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 2. 응답
			var response = new S_EquipmentData();
			response.Slots.AddRange( session.Player.ToEquipmentRefs() );

			_room.SendToPlayer( session, response );

			_logger.LogDebug( "Player {PlayerId} requested equipment data", session.PlayerId );
		}

		/// <summary>
		/// C_UseItem 패킷 처리
		/// 소비 아이템 사용 (HP/MP 회복 등)
		/// </summary>
		private async Task HandleC_UseItemAsync( IClientSession session, C_UseItem packet )
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "UseItem validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 3. 아이템 사용
			bool success = session.Player.UseItem(packet.InstanceId, packet.Quantity);
			if ( !success )
			{
				// 4. 실패 응답
				_room.SendToPlayer( session, new S_UseItem { Success = success, Message = "아이템 사용 실패" } );
				return;
			}

			_room.RequestPlayerCheckpoint( session.Player, "UseItem" );

			_logger.LogDebug( "Player {PlayerId} used item at InstanceId {InstanceId}, Success={Success}",
				session.PlayerId, packet.InstanceId, success );
		}

		/// <summary>
		/// C_EquipItem 패킷 처리
		/// 인벤토리 아이템을 장비 슬롯에 장착
		/// </summary>
		private async Task HandleC_EquipItemAsync( IClientSession session, C_EquipItem packet )
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "EquipItem validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 3. 장비 착용 (이벤트 발생: OnItemEquipped → s_equipmentUpdate 자동 전송)
			bool success = session.Player.EquipItemFromInventory(packet.InstanceId, packet.EquipSlot);

			// 4. 실패 시에만 에러 응답 (성공은 이벤트에서 처리)
			if(!success)
			{
				_room.SendToPlayer( session, new S_EquipmentUpdate { Success = false, Reason = "장비 장착에 실패했습니다." } );
				_logger.LogWarning( "Player {PlayerId} failed to equip item at InstanceId {InstanceId}",
					session.PlayerId, packet.InstanceId );
				return;
			}

			_room.RequestPlayerCheckpoint( session.Player, "EquipItem" );
		}

		/// <summary>
		/// C_UnequipItem 패킷 처리
		/// 장착된 장비를 인벤토리로 해제
		/// </summary>
		private async Task HandleC_UnequipItemAsync( IClientSession session, C_UnequipItem packet )
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "UnequipItem validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 3. 장비 해제 (이벤트 발생: OnItemUnequipped → S_EquipmentUpdate 자동 전송)
			var equipSlot = (Server.Game.PlayerEquipment.EquipSlot)packet.EquipSlot;
			bool success = session.Player.UnequipItemToInventory(equipSlot);

			// 4. 실패 시에만 에러 응답 (성공은 이벤트에서 처리)
			if(!success)
			{
				_room.SendToPlayer( session, new S_EquipmentUpdate { Success = false, Reason = "장비 해제에 실패했습니다." } );
				_logger.LogWarning( "Player {PlayerId} failed to unequip item at slot {Slot}",
					session.PlayerId, packet.EquipSlot );
				return;
			}

			_room.RequestPlayerCheckpoint( session.Player, "UnequipItem" );
		}
	}
}
