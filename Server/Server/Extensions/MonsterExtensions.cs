using Protocol;
using Server.Game;
using Server.Game.Monsters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Extensions
{
	public static class MonsterExtensions
	{
		/// <summary>
		/// Monster의 Info를 Protocol ObjectInfo로 변환
		/// </summary>
		public static ObjectInfo ToObjectInfo( this Monster monster )
		{
			if(monster == null)
				ArgumentNullException.ThrowIfNull( monster );

			return new ObjectInfo
			{
				ObjectId = monster.MonsterId,
				Type = ObjectType.ObjectMonster,
				MonsterInfo = monster.Info.Clone()
			};
		}

		public static ObjectDamageInfo ToObjectDamageInfo( this Monster monster, int damage, bool isCritical )
		{
			if(monster == null)
				ArgumentNullException.ThrowIfNull( monster );

			return new ObjectDamageInfo
			{
				ObjectId = monster.MonsterId,
				Damage = damage,
				IsCritical = isCritical,
				CurrentHP = monster.CurrentHP,
				Type = ObjectType.ObjectMonster,
			};
		}
	}
}
