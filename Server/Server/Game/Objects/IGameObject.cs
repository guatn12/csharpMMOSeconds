using Protocol;
using System;

namespace Server.Game.Objects
{
	public interface IGameObject
	{
		long ObjectId { get; }
		string Name { get; }
		ObjectType Type { get; }
		PosInfo PosInfo { get; }
		State CreatureState { get; }
		StatInfo Stats { get; }
		bool IsAlive { get; }
		DateTime LastUpdateTime { get; }


		bool TakeDamage( int damage, long attackerId );
		bool Heal( int amount );
		void UpdatePosition( PosInfo newPosition );
		ObjectInfo ToObjectInfo();

		// 이벤트
		event Action<IGameObject> OnDeath;										// 사망 처리 이벤트
		event Action<IGameObject, int, int> OnHealthChanged;					// 체력 변경 이벤트
		event Action<IGameObject, int, int> OnStateChanged;						// 상태 변경 이벤트
	}
}
