using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game.Objects
{
	public class GameObject : IGameObject
	{


		public long ObjectId => throw new NotImplementedException();

		public ObjectType Type => throw new NotImplementedException();

		public PosInfo PosInfo => throw new NotImplementedException();

		public State CreatureState => throw new NotImplementedException();

		public StatInfo Stats => throw new NotImplementedException();

		public bool IsAlive => throw new NotImplementedException();

		public DateTime LastUpdateTime => throw new NotImplementedException();

		public event Action<IGameObject> OnDeath;
		public event Action<IGameObject, int, int> OnHealthChanged;
		public event Action<IGameObject, int, int> OnStateChanged;

		public bool Heal( int amount )
		{
			throw new NotImplementedException();
		}

		public bool TakeDamage( int damage, long attackerId )
		{
			throw new NotImplementedException();
		}

		public ObjectInfo ToObjectInfo()
		{
			throw new NotImplementedException();
		}

		public void UpdatePosition( PosInfo newPosition )
		{
			throw new NotImplementedException();
		}
	}
}
