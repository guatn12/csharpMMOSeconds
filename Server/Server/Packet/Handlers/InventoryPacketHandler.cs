using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Extensions;
using Server.Room;
using Server.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
		private async Task HandleC_InventoryRequestAsync(GameSession session, C_InventoryRequest packet)
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

			await _room.SendToPlayerAsync( session, response );

			_logger.LogDebug( "Player {PlayerId} requested inventory data", session.PlayerId );
		}

		/// <summary>
		/// C_UseItem 패킷 처리
		/// 소비 아이템 사용 (HP/MP 회복 등)
		/// </summary>
		private async Task HandleC_UseItemAsync( GameSession session, C_UseItem packet )
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "UseItem validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 2. 슬롯 검증
			var slotValidation = PacketValidators.ValidateItemSlot(packet.Slot);
			if(!slotValidation.IsValid)
			{
				_logger.LogWarning( "UseItem slot validation failed: {Error}", slotValidation.ErrorMessage );

				var errorResponse = new S_UseItem { Success = false };
				await _room.SendToPlayerAsync( session, errorResponse );
				return;
			}

			// 3. 아이템 사용
			bool success = session.Player.UseItem(packet.Slot, packet.Quantity);

			// 4. 응답
			var item = session.Player.Inventory.GetItem(packet.Slot);
			var response = new S_UseItem
			{
				Success = success,
				Slot = packet.Slot,
				RemainingQuantity = success ? (item?.Quantity ?? 0) : 0,
				Message = success ? "아이템 사용 성공" : "아이템 사용 실패"
			};

			await _room.SendToPlayerAsync( session, response );

			_logger.LogDebug( "Player {PlayerId} used item at slot {Slot}, Success={Success}",
				session.PlayerId, packet.Slot, success );
		}

		/// <summary>
		/// C_EquipItem 패킷 처리
		/// 인벤토리 아이템을 장비 슬롯에 장착
		/// </summary>
		private async Task HandleC_EquipItemAsync( GameSession session, C_EquipItem packet )
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "EquipItem validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 2. 슬롯 검증
			var slotValidation = PacketValidators.ValidateItemSlot(packet.InventorySlot);
			if(!slotValidation.IsValid)
			{
				_logger.LogWarning( "EquipItem slot validation failed: {Error}", slotValidation.ErrorMessage );

				await _room.SendToPlayerAsync( session, new S_ItemEquipped { Success = false } );
				return;
			}

			// 3. 장비 착용 (이벤트 발생: OnItemEquipped → S_ItemEquipped 자동 전송)
			bool success = session.Player.EquipItemFromInventory(packet.InventorySlot);

			// 4. 실패 시에만 에러 응답 (성공은 이벤트에서 처리)
			if(!success)
			{
				await _room.SendToPlayerAsync( session, new S_ItemEquipped { Success = false } );
				_logger.LogWarning( "Player {PlayerId} failed to equip item at slot {Slot}",
					session.PlayerId, packet.InventorySlot );
			}
		}

		/// <summary>
		/// C_UnequipItem 패킷 처리
		/// 장착된 장비를 인벤토리로 해제
		/// </summary>
		private async Task HandleC_UnequipItemAsync( GameSession session, C_UnequipItem packet )
		{
			// 1. 기본 검증
			var validation = PacketValidators.ValidateBasic(session, _room);
			if(!validation.IsValid)
			{
				_logger.LogWarning( "UnequipItem validation failed: {Error}", validation.ErrorMessage );
				return;
			}

			// 2. 장비 슬롯 검증
			var slotValidation = PacketValidators.ValidateEquipSlot(packet.EquipSlot);
			if(!slotValidation.IsValid)
			{
				_logger.LogWarning( "UnequipItem slot validation failed: {Error}", slotValidation.ErrorMessage );

				await _room.SendToPlayerAsync( session, new S_ItemUnequipped { Success = false } );
				return;
			}

			// 3. 장비 해제 (이벤트 발생: OnItemUnequipped → S_ItemUnequipped 자동 전송)
			var equipSlot = (Server.Game.PlayerEquipment.EquipSlot)packet.EquipSlot;
			bool success = session.Player.UnequipItemToInventory(equipSlot);

			// 4. 실패 시에만 에러 응답 (성공은 이벤트에서 처리)
			if(!success)
			{
				await _room.SendToPlayerAsync( session, new S_ItemUnequipped { Success = false } );
				_logger.LogWarning( "Player {PlayerId} failed to unequip item at slot {Slot}",
					session.PlayerId, packet.EquipSlot );
			}
		}
	}
}
