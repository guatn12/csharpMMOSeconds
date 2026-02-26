using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game.Objects
{
	public abstract class GameObject : IGameObject
	{
		protected readonly object _lock = new object();
		protected readonly long _objectId;
		protected readonly ObjectType _type;
		protected string _name;
		protected PosInfo _posInfo = new PosInfo();
		protected State _creatureState = State.Idle;
		protected StatInfo _statInfo = new StatInfo();
		protected DateTime _lastUpdateTime = DateTime.UtcNow;

		public long ObjectId => _objectId;
		public long ObjectRawId => GameObjectId.GetRawId(ObjectId);
		public ObjectType Type => _type;
		public PosInfo PosInfo => _posInfo;
		public State CreatureState => _creatureState;
		public StatInfo Stats => _statInfo;
		public bool IsAlive => CreatureState != State.Dead && 0 < Stats.CurrentHP;
		public DateTime LastUpdateTime => _lastUpdateTime;
		public int CurrentHP => Stats.CurrentHP;
		public int MaxHP => Stats.MaxHP;
		public int CurrentMP => Stats.CurrentMP;
		public int MaxMP => Stats.MaxMP;
		public int Level => Stats.Level;
		public string Name => _name;

		public event Action<IGameObject> OnDeath;
		public event Action<IGameObject, int, int> OnHealthChanged;
		public event Action<IGameObject, int, int> OnStateChanged;

		public GameObject(long objectId, ObjectType type)
		{
			_objectId = objectId;
			_type = type;
		}

		public abstract bool Heal( int amount );
		public abstract bool TakeDamage( int damage, long attackerId );
		public abstract ObjectInfo ToObjectInfo();

		public virtual void UpdatePosition( PosInfo newPosition )
		{
			if(newPosition == null) return;

			_posInfo = newPosition;
			UpdateLastUpdateTime();
		}

		public void SetState(State newState)
		{
			lock(_lock)
			{
				if(newState == CreatureState) return;

				var oldState = CreatureState;
				_creatureState = newState;

				OnStateChanged.Invoke( this, (int)oldState, (int)newState );
			}

			UpdateLastUpdateTime();
		}

		public void UpdateLastUpdateTime()
		{
			_lastUpdateTime = DateTime.UtcNow;
		}

		protected void RaiseOnDeath()
		{
			OnDeath.Invoke( this );
		}

		protected void RaiseOnHealthChanged(int oldValue, int newValue)
		{
			OnHealthChanged.Invoke( this, oldValue, newValue );
		}
	}
}
