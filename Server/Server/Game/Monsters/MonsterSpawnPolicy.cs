using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game.Monsters
{
	/// <summary>
	/// 몬스터 스폰 정책 클래스
	/// Room 타입별로 다른 스폰 정책을 적용할 수 있음
	/// </summary>
	public class MonsterSpawnPolicy
	{
		public int MaxMonsters { get; set; } = 50;

		public TimeSpan DefaultRespawnInterval { get; set; } = TimeSpan.FromSeconds(5);

		public bool AutoRespawn { get; set; } = true;

		public bool ScaleWithPlayerCount { get; set; } = false;

		public int MonstersPerPlayer { get; set; } = 2;

		#region Preset Policies (사전 정의된 정책)

		public static MonsterSpawnPolicy Default => new MonsterSpawnPolicy
		{
			MaxMonsters = 50,
			DefaultRespawnInterval = TimeSpan.FromSeconds( 5 ),
			AutoRespawn = true,
			ScaleWithPlayerCount = false
		};

		public static MonsterSpawnPolicy LobbyDefault => new MonsterSpawnPolicy
		{
			MaxMonsters = 10,
			DefaultRespawnInterval = TimeSpan.FromSeconds( 10 ),
			AutoRespawn = true,
			ScaleWithPlayerCount = false
		};

		public static MonsterSpawnPolicy BattleDefault => new MonsterSpawnPolicy
		{
			MaxMonsters = 50,
			DefaultRespawnInterval = TimeSpan.FromSeconds( 5 ),
			AutoRespawn = true,
			ScaleWithPlayerCount = true,
			MonstersPerPlayer = 3
		};

		public static MonsterSpawnPolicy RaidDefault => new MonsterSpawnPolicy
		{
			MaxMonsters = 100,
			DefaultRespawnInterval = TimeSpan.FromMinutes( 5 ),  // 레이드 보스는 5분 리스폰
			AutoRespawn = false,  // 보스는 수동 리스폰
			ScaleWithPlayerCount = false
		};

		#endregion
	}
}
