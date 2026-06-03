using Server.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data
{
	public interface IDataManager
	{
		GameConfigData GameConfig { get; }
		bool IsDataLoaded { get; }
		DateTime LastLoadTime { get; }

		// 조회 (단건)
		ItemData GetItem( int itemId );
		MonsterData GetMonster( int monsterId );
		SkillData GetSkill( int skillId );
		MapData GetMap( int mapId );

		// 조회 (전체)
		IReadOnlyDictionary<int, ItemData> GetAllItems();
		IReadOnlyDictionary<int, MonsterData> GetAllMonsters();
		IReadOnlyDictionary<int, SkillData> GetAllSkills();
		IReadOnlyDictionary<int, MapData> GetAllMaps();

		// 통계
		int GetTotalItemCount();
		int GetTotalMonsterCount();
		int GetTotalSkillCount();
		int GetTotalMapCount();

		// 필터링
		IEnumerable<ItemData> GetItemsByGrade( string grade );
		IEnumerable<MonsterData> GetMonstersByType( string monsterType );
		IEnumerable<SkillData> GetSkillsByType( string skillType );
		IEnumerable<SkillData> GetMonsterSkills( int monsterId );

		// 데이터 로드 / 검증
		Task<bool> LoadAllDataAsync();
		bool ValidateAllData();
	}
}
