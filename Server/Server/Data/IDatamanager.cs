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
		ItemData? GetItem( int itemId );
		MonsterData? GetMonster( int monsterId );
		SkillData? GetSkill( int skillId );

		IReadOnlyDictionary<int, ItemData> GetAllItems();
		IReadOnlyDictionary<int, MonsterData> GetAllMonsters();
		IReadOnlyDictionary<int, SkillData> GetAllSkills();

		Task<bool> LoadAllDataAsync();
		bool ValidateAllData();

		int GetTotalItemCount();
		int GetTotalMonsterCount();
		int GetTotalSkillCount();

		IEnumerable<ItemData> GetItemsByGrade( string grade );
		IEnumerable<MonsterData> GetMonstersByType( string monsterType );
		IEnumerable<SkillData> GetSkillsByType( string skillType );
		IEnumerable<SkillData> GetMonsterSkills( int monsterId );

		bool IsDataLoaded { get; }
		DateTime LastLoadTime { get; }
	}
}
