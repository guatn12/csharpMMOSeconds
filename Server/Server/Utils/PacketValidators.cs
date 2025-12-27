using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Game.Monsters;
using Server.Room;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Utils
{
	/// <summary>
	/// 패킷 검증 유틸
	/// </summary>
	public static class PacketValidators
	{
		/// <summary>
		/// 기본 검증: 세션, 룸 Null 체크
		/// </summary>
		public static ValidationResult ValidateBasic(GameSession session, BaseRoom room)
		{
			if(session == null)
				return ValidationResult.Failure( "세션이 null입니다." );

			if(room == null)
				return ValidationResult.Failure( "룸이 null입니다." );

			if(!room.ContainsPlayer( session ))
				return ValidationResult.Failure( "플레이어가 룸에 없습니다." );

			return ValidationResult.Success();
		}

		public static ValidationResult ValidateRange(PosInfo posInfo, BaseRoom room)
		{
			if(posInfo == null)
				return ValidationResult.Failure( "위치 정보가 null 입니다." );

			if(!Position3DValidator.IsValidPosition( posInfo, room ))
				return ValidationResult.Failure( "위치 정보가 잘못되었습니다." );

			return ValidationResult.Success();
		}

		/// <summary>
		/// 아이템 슬롯 검증
		/// </summary>
		public static ValidationResult ValidateItemSlot(int slot, int maxSlots = 50)
		{
			if(slot < 0 || maxSlots <= slot)
				return ValidationResult.Failure( $"잘못된 슬롯 번호: {slot}" );

			return ValidationResult.Success();
		}

		/// <summary>
		/// 장비 슬롯 검증
		/// </summary>
		public static ValidationResult ValidateEquipSlot(int slot, int maxSlots = (int)Game.PlayerEquipment.EquipSlot.Earring )
		{
			if(slot < 1 || maxSlots < slot)
				return ValidationResult.Failure($"잘못된 장비 슬롯: {slot}");

			return ValidationResult.Success();
		}

		/// <summary>
		/// 몬스터 존재 검증
		/// </summary>
		public static ValidationResult ValidateMonster(Monster monster)
		{
			if(monster == null)
				return ValidationResult.Failure( "몬스터가 존재하지 않습니다." );

			if(!monster.IsAlive)
				return ValidationResult.Failure( "몬스터가 이미 사망했습니다." );

			return ValidationResult.Success();
		}
	}
}
