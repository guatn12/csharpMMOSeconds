using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DummyClient.Config;
using DummyClient.Data.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DummyClient.Data
{
	public class DataManager
	{
		private readonly ILogger<DataManager> _logger;
		private readonly GameDataConfig _gameDataConfig;

		private readonly ConcurrentDictionary<int, ItemData> _items = new ConcurrentDictionary<int, ItemData>();
		private readonly ConcurrentDictionary<int, MonsterData> _monsters = new ConcurrentDictionary<int, MonsterData>();
		private readonly ConcurrentDictionary<int, SkillData> _skills = new ConcurrentDictionary<int, SkillData>();

		private bool _isDataLoaded = false;
		private DateTime _lastLoadTime = DateTime.MinValue;
		private readonly object _lock = new object();

		public DataManager(
			GameDataConfig gameDataConfig,
			ILogger<DataManager> logger )
		{
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			_gameDataConfig = gameDataConfig;
			_logger.LogInformation( "DataManager initialized with data path: {DataPath}", _gameDataConfig.DataPath );
		}

		public ItemData? GetItem( int itemId )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetItem called but data is not loaded yet" );
				return null;
			}

			return _items.TryGetValue( itemId, out var item ) ? item : null;
		}

		public MonsterData? GetMonster( int monsterId )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetMonster called but data is not loaded yet" );
				return null;
			}

			return _monsters.TryGetValue( monsterId, out var monster ) ? monster : null;
		}

		public SkillData? GetSkill( int skillId )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetSkill called but data is not loaded yet" );
				return null;
			}

			return _skills.TryGetValue( skillId, out var skill ) ? skill : null;
		}

		public IReadOnlyDictionary<int, ItemData> GetAllItems()
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetAllItems called but data is not loaded yet" );
				return new Dictionary<int, ItemData>();
			}

			return _items;
		}

		public IReadOnlyDictionary<int, MonsterData> GetAllMonsters()
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetAllMonsters called but data is not loaded yet" );
				return new Dictionary<int, MonsterData>();
			}

			return _monsters;
		}

		public IReadOnlyDictionary<int, SkillData> GetAllSkills()
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetAllSkills called but data is not loaded yet" );
				return new Dictionary<int, SkillData>();
			}

			return _skills;
		}

		public int GetTotalItemCount()
		{
			return _isDataLoaded ? _items.Count : 0;
		}

		public int GetTotalMonsterCount()
		{
			return _isDataLoaded ? _monsters.Count : 0;
		}

		public int GetTotalSkillCount()
		{
			return _isDataLoaded ? _skills.Count : 0;
		}

		public IEnumerable<ItemData> GetItemsByGrade( string grade )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetItemByGrade called but data is not loaded yet" );
				return Enumerable.Empty<ItemData>();
			}

			return _items.Values
				.Where( item => item.Grade.Equals( grade, StringComparison.OrdinalIgnoreCase ) );
		}

		public IEnumerable<MonsterData> GetMonstersByType( string monsterType )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetMonstersByType called but data is not loaded yet" );
				return Enumerable.Empty<MonsterData>();
			}

			return _monsters.Values
				.Where( monster => monster.MonsterType.Equals( monsterType, StringComparison.OrdinalIgnoreCase ) );
		}

		public IEnumerable<SkillData> GetSkillsByType( string skillType )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetSkillsByType called but data is not loaded yet" );
				return Enumerable.Empty<SkillData>();
			}

			return _skills.Values
				.Where( skill => skill.SkillType.Equals( skillType, StringComparison.OrdinalIgnoreCase ) );
		}

		public IEnumerable<SkillData> GetMonsterSkills( int monsterId )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetMonsterSkills called but data is not loaded yet" );
				return Enumerable.Empty<SkillData>();
			}

			MonsterData monster = GetMonster(monsterId);
			if(monster == null || monster.Skills == null || monster.Skills.Count == 0)
			{
				return Enumerable.Empty<SkillData>();
			}

			var skills = new List<SkillData>();
			foreach(int skillId in monster.Skills)
			{
				SkillData skill = GetSkill(skillId);
				if(skill != null)
				{
					skills.Add( skill );
				}
			}

			return skills;
		}

		public bool IsDataLoaded => _isDataLoaded;
		public DateTime LastLoadTime => _lastLoadTime;

		public async Task<bool> LoadAllDataAsync()
		{
			lock(_lock)
			{
				if(_isDataLoaded)
				{
					_logger.LogInformation( "Data is already loaded. Skipping reload." );
					return true;
				}
			}

			_logger.LogInformation( "Starting to load all game data..." );

			try
			{
				string itemsFilePath = _gameDataConfig.GetDataFilePath("items");
				string monsterFilePath = _gameDataConfig.GetDataFilePath("monsters");
				string skillsFilePath = _gameDataConfig.GetDataFilePath("skills");

				// 경로 확인 (필요시 디버그 용도)
				_logger.LogDebug( "Current working directory: {WorkingDir}", Directory.GetCurrentDirectory() );
				_logger.LogDebug( "Resolved items file path: {ItemsPath}", itemsFilePath );

				// 모든 파일 존재 확인
				if(!File.Exists( itemsFilePath ))
				{
					_logger.LogError( "Items data file not found: {FilePath}", itemsFilePath );
					return false;
				}

				if(!File.Exists( monsterFilePath ))
				{
					_logger.LogError( "Monsters data file not found: {FilePath}", monsterFilePath );
					return false;
				}

				if(!File.Exists( skillsFilePath ))
				{
					_logger.LogError( "Skills data file not found: {FilePath}", skillsFilePath );
					return false;
				}

				//json 파일 로드
				string itemsJson = await File.ReadAllTextAsync(itemsFilePath);
				string monstersJson = await File.ReadAllTextAsync(monsterFilePath);
				string skillsJson = await File.ReadAllTextAsync(skillsFilePath);

				// json 역직렬화
				List<ItemData> items = JsonSerializer.Deserialize<List<ItemData>>(itemsJson);
				List<MonsterData> monsters = JsonSerializer.Deserialize<List<MonsterData>>(monstersJson);
				List<SkillData> skills = JsonSerializer.Deserialize<List<SkillData>>(skillsJson);

				if(items == null || monsters == null || skills == null)
				{
					_logger.LogError( "Filaed to deserialize game data files" );
					return false;
				}

				// dictionary 변환
				Dictionary<int, ItemData> itemsDict = items.ToDictionary(item => item.Id);
				Dictionary<int, MonsterData> monstersDict = monsters.ToDictionary(monster => monster.Id);
				Dictionary<int, SkillData> skillsDict = skills.ToDictionary(skill => skill.Id);

				// 스레드 안전하게 데이터 업데이트
				UpdateData(itemsDict, monstersDict, skillsDict);

				lock(_lock)
				{
					_isDataLoaded = true;
					_lastLoadTime = DateTime.UtcNow;
				}

				_logger.LogInformation( "Successfully loaded game data" );

				return true;
			}
			catch(JsonException ex)
			{
				_logger.LogError( ex, "Json parsing error while loading game data" );
				return false;
			}
			catch(IOException ex)
			{
				_logger.LogError( ex, "File I/O error while loading game data" );
				return false;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Unexcepted error while loading game data" );
				return false;
			}
		}

		public bool ValidateAllData()
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "ValidateAllData called but data is not loaded yet" );
				return false;
			}

			_logger.LogInformation( "Starting data validation..." );
			bool isValid = true;
			int totalErrors = 0;

			// Items 검증
			IReadOnlyDictionary<int, ItemData> items = _items;
			List<int> invalidItems = new List<int>();

			foreach(var kvp in items)
			{
				var item = kvp.Value;

				// 기본 검증
				if(!item.IsValid())
				{
					invalidItems.Add( item.Id );
					_logger.LogError( "Invalid item data: ID={ItemId}, Name={Name}", item.Id, item.Name );
					isValid = false;
					totalErrors++;
				}

				// ID 일치성 검증(Dictionary Key와 실제 ID가 같은지)
				if(kvp.Key != item.Id)
				{
					_logger.LogError( "Item ID mismatch: Key={Key}, ActualId={ActualId}", kvp.Key, item.Id );
					isValid = false;
					totalErrors++;
				}
			}

			// Monsters 검증
			IReadOnlyDictionary<int, MonsterData> monsters = _monsters;
			List<int> invalidMonsters = new List<int>();

			foreach(var kvp in monsters)
			{
				var monster = kvp.Value;

				// 기본 검증
				if(!monster.IsValid())
				{
					invalidMonsters.Add( monster.Id );
					_logger.LogError( "Invalid monster data: ID={MonsterId}, Name={Name}", monster.Id, monster.Name );
					isValid = false;
					totalErrors++;
				}

				// ID 일치성 검증(Dictionary Key와 실제 ID가 같은지)
				if(kvp.Key != monster.Id)
				{
					_logger.LogError( "Monster ID mismatch: Key={Key}, ActualId={ActualId}", kvp.Key, monster.Id );
					isValid = false;
					totalErrors++;
				}

				// 스킬 참조 무결성 검증
				if(monster.Skills != null && 0 < monster.Skills.Count)
				{
					foreach(int skillId in monster.Skills)
					{
						if(!_skills.ContainsKey( skillId ))
						{
							_logger.LogError( "Monster {MonsterId} references non-existent skill {SkillId}", monster.Id, skillId );
							isValid = false;
							totalErrors++;
						}
					}
				}
			}

			// Skills 검증
			IReadOnlyDictionary<int, SkillData> skills = _skills;
			List<int> invalidSkills = new List<int>();

			foreach(var kvp in skills)
			{
				var skill = kvp.Value;

				// 기본 검증
				if(!skill.IsValid())
				{
					invalidSkills.Add( skill.Id );
					_logger.LogError( "Invalid skill data: ID={SkillId}, Name={Name}", skill.Id, skill.Name );
					isValid = false;
					totalErrors++;
				}

				// ID 일치성 검증(Dictionary Key와 실제 ID가 같은지)
				if(kvp.Key != skill.Id)
				{
					_logger.LogError( "Skill ID mismatch: Key={Key}, ActualId={ActualId}", kvp.Key, skill.Id );
					isValid = false;
					totalErrors++;
				}
			}

			// 검증 결과 로깅
			if(isValid)
			{
				_logger.LogInformation( "Data validation completed successfully. Items: {ItemCount}, Monsters: {MonsterCount}, Skills: {SkillCount}",
			  items.Count, monsters.Count, skills.Count );
			}
			else
			{
				_logger.LogError( "Data validation failed with {ErrorCount} errors. Invalid Items: {InvalidItemCount}, Invalid Monsters:{ InvalidMonsterCount}, Invalid Skills: { InvalidSkillCount}",
						totalErrors, invalidItems.Count, invalidMonsters.Count, invalidSkills.Count );
			}

			return isValid;
		}

		private void UpdateData( Dictionary<int, ItemData> itemDict,
			Dictionary<int, MonsterData> monsterDict,
			Dictionary<int, SkillData> skillDict )
		{
			_items.Clear();
			_monsters.Clear();
			_skills.Clear();

			foreach(var kvp in itemDict) _items[ kvp.Key ] = kvp.Value;
			foreach(var kvp in monsterDict) _monsters[ kvp.Key ] = kvp.Value;
			foreach(var kvp in skillDict) _skills[ kvp.Key ] = kvp.Value;
		}
	}
}
