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
		public static ValidationResult ValidateBasic( IClientSession session, IRoom room)
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

		public static ValidationResult ValidatePlayerName(string name)
		{
			if(string.IsNullOrWhiteSpace( name ))
				return ValidationResult.Failure( "이름이 비어 있습니다." );
			if(name.Length < 2 || 16 < name.Length)
				return ValidationResult.Failure( "이름은 2 ~ 16자여야 합니다." );
			if(System.Text.RegularExpressions.Regex.IsMatch( name, @"^[가-힣a-zA-Z0-9]+$" ) == false)
				return ValidationResult.Failure( "한글/영문/숫자만 사용할 수 있습니다." );
			if(ContainsForbiddenWord( name ))
				return ValidationResult.Failure( "금지어가 포함되어 있습니다" );
			return ValidationResult.Success();
		}

		private static readonly HashSet<string> _forbiddenWords = new()
		{
			"운영자", "관리자", "Admin", "GM", "Operator",
		};

		private static bool ContainsForbiddenWord(string name)
		{
			string lowerd = name.ToLowerInvariant();
			return _forbiddenWords.Any( w => lowerd.Contains( w.ToLowerInvariant() ) );
		}
	}
}
