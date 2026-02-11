using Protocol;
using System.Threading;

namespace Server.Game.Objects
{
	public static class GameObjectId
	{
		// 상위 8비트를 타입 / 하위 56비트는 value
		private const int TypeShift = 56;
		// 상위 8비트와 하위 56비트 구분을 위한 mask
		private const long IdMask = 0x00FF_FFFF_FFFF_FFFF;

		/// <summary>
		/// bitMask를 통해 타입을 상위 8비트로 시프트/ rawId는 상위 8비트를 & 연산을 통해 0처리 하고 나머지 값을 or 연산
		/// </summary>
		/// <param name="type"></param>
		/// <param name="rawId"></param>
		/// <returns></returns>
		public static long Generate(ObjectType type, long rawId)
		{
			return ((long)type << TypeShift) | (rawId & IdMask);
		}

		/// <summary>
		/// left 시프트를 통해 상위 8비트를 56비트를 이동/ 상위 56비트는 0이되고 하위 8비트가 type
		/// </summary>
		/// <param name="objectId"></param>
		/// <returns></returns>
		public static ObjectType GetType(long objectId)
		{
			return (ObjectType)(objectId >> TypeShift);
		}

		/// <summary>
		/// &연산을 통해 실 ID값을 추출
		/// </summary>
		/// <param name="objectId"></param>
		/// <returns></returns>
		public static long GetRawId(long objectId)
		{
			return objectId & IdMask;
		}

		// 런타임 객체용 시퀸스 (몬스터 등)
		private static long _monsterSequence = 0;
		public static long GenerateMonsterId()
		{
			long seq = Interlocked.Increment(ref _monsterSequence);
			return Generate( ObjectType.ObjectMonster, seq );
		}
	}
}
