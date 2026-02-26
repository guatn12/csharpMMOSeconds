using Protocol;
using Server.Game;
using Server.Game.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Extensions
{
	public static class GameObjectExtensions
	{
		public static ObjectDamageInfo ToObjectDamageInfo( this GameObject gameObject, int damage, bool isCritical )
		{
			if(gameObject == null)
				ArgumentNullException.ThrowIfNull( gameObject );

			return new ObjectDamageInfo
			{
				ObjectId = gameObject.ObjectId,
				Damage = damage,
				IsCritical = isCritical,
				CurrentHP = gameObject.CurrentHP,
				Type = gameObject.Type,
			};
		}
	}
}
