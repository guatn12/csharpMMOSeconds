using Protocol;
using Server.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Extensions
{
	public static class PlayerExtensions
	{
		#region Equipment 변환

		/// <summary>
		/// Player의 장비 정보를 Protocol EquipmentInfo로 변환
		/// </summary>
		public static EquipmentInfo ToEquipmentInfo(this Player player)
		{
			if(player == null)
				throw new ArgumentNullException(nameof(player));

			var equipmentData = player.GetEquipmentData();
			var result = new EquipmentInfo();

			// 장착된 장비를 Protocol 메시지로 변환
			foreach( var equipment in equipmentData )
			{
				switch(equipment.Key)
				{
				case PlayerEquipment.EquipSlot.Weapon:
					result.WeaponItemId = equipment.Value?.ItemId ?? 0;
					break;
				case PlayerEquipment.EquipSlot.Armor:
					result.ArmorItemId = equipment.Value?.ItemId ?? 0;
					break;

				case PlayerEquipment.EquipSlot.Helmet:
					result.HelmetItemId = equipment.Value?.ItemId ?? 0;
					break;

				case PlayerEquipment.EquipSlot.Gloves:
					result.GlovesItemId = equipment.Value?.ItemId ?? 0;
					break;

					// 추가적인 슬롯이 있을 경우 여기에 추가.
				}
			}

			return result;
		}
		
		/// <summary>
		/// Player의 장비 정보를 Protocol EquipmentInfo로 변환 (Null-Safe)
		/// </summary>
		public static EquipmentInfo ToEquipmentInfoOrEmpty(this Player player)
		{
			return player == null ? new EquipmentInfo() : player.ToEquipmentInfo();
		}

		#endregion

		#region Stats 변환

		/// <summary>
		/// Player의 현재 스탯을 Protocol PlayerStats로 변환
		/// </summary>
		public static PlayerStats ToPlayerStats(this Player player)
		{
			if(player == null) 
				throw new ArgumentNullException(nameof (player));

			return new PlayerStats
			{
				Attack = player.GetTotalAttack(),   // 기본 공격력 + 장비 공격력
				Defense = player.GetTotalDefense(), // 기본 방어력 + 장비 방어력
				MaxHP = player.MaxHP,               // 최대 HP
				MaxMP = player.MaxMP,               // 최대 MP
				CurrentHP = player.CurrentHP,       // 현재 HP
				CurrentMP = player.CurrentMP,       // 현재 MP
			};
		}

		/// <summary>
		/// Player의 현재 스탯을 Protocol PlayerStats로 변환 (Null-Safe)
		/// </summary>
		public static PlayerStats ToPlayerStatsOrEmpty(this Player player)
		{
			return player == null ? new PlayerStats() : player.ToPlayerStats();
		}
		#endregion

		#region Utility 메서드
		
		/// <summary>
		/// 장비 착용/해제 시 스탯이 실제로 변경되었는지 확인
		/// - 스탯 변경 시에만 브로드캐스트하여 불필요한 패킷 전송 방지
		/// </summary>
		public static bool HasStatsChanged(this Player player, int oldAttack, int oldDefense, int oldMaxHP, int oldMaxMP)
		{
			if(player == null)
				return false;

			return player.GetTotalAttack() != oldAttack || 
				player.GetTotalDefense() != oldDefense ||
				player.MaxHP != oldMaxHP ||
				player.MaxMP != oldMaxMP;
		}

		/// <summary>
		/// Player가 착용 가능한 장비를 가지고 있는지 확인
		/// </summary>
		public static bool HasEquippableItems(this Player player)
		{
			if(player?.Inventory == null)
				return false;

			// 인벤토리에서 착용 가능한 아이템이 있는지 확인
			// 실제 구현은 ItemData.ItemType, 현재 장착 아이템의 레벨, 등급 등의 비교가 필요.
			// 현재는 간단히 인벤토리 아이템 존재 여부만 확인
			return 0 < player.Inventory.GetAllItems().Count;
		}

		/// <summary>
		/// Player의 장비 슬롯이 비어있는지 확인
		/// </summary>
		public static bool IsEquipSlotEmpty(this Player player, PlayerEquipment.EquipSlot slot)
		{
			if(player?.Equipment == null)
				return true;

			return !player.Equipment.IsSlotEquipped( slot );
		}
		#endregion
	}
}
