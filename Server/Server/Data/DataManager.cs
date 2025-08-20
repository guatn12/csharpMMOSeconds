using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Configuration;
using Server.Data.Models;
using Server.Data.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Server.Data
{
	public class DataManager : IDataManager
	{
		private readonly IDataStorageProvider _storageProvider;
		private readonly ILogger<DataManager> _logger;
		private readonly GameDataSettings _gameDataSettings;

		private bool _isDataLoaded = false;
		private DateTime _lastLoadTime = DateTime.MinValue;
		private readonly object _lock = new object();

		public DataManager(
			IDataStorageProvider storageProvider,
			IOptions<GameDataSettings> gameDataOptions,
			ILogger<DataManager> logger )
		{
			_storageProvider = storageProvider ?? throw new ArgumentNullException( nameof( storageProvider ) );
			
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );

			_gameDataSettings = gameDataOptions.Value;

			_logger.LogInformation( "DataManager initialized with data path: {DataPath}", _gameDataSettings.DataPath );
		}

		public ItemData? GetItem( int itemId )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetItem called but data is not loaded yet" );
				return null;
			}

			return _storageProvider.Items.Get( itemId );
		}

		public MonsterData? GetMonster( int monsterId )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetMonster called but data is not loaded yet" );
				return null;
			}

			return _storageProvider.Monsters.Get( monsterId );
		}

		public SkillData? GetSkill( int skillId )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetSkill called but data is not loaded yet" );
				return null;
			}

			return _storageProvider.Skills.Get( skillId );
		}

		public IReadOnlyDictionary<int, ItemData> GetAllItems()
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetAllItems called but data is not loaded yet" );
				return new Dictionary<int, ItemData>();
			}

			return _storageProvider.Items.GetAll();
		}

		public IReadOnlyDictionary<int, MonsterData> GetAllMonsters()
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetAllMonsters called but data is not loaded yet" );
				return new Dictionary<int, MonsterData>();
			}

			return _storageProvider.Monsters.GetAll();
		}

		public IReadOnlyDictionary<int, SkillData> GetAllSkills()
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetAllSkills called but data is not loaded yet" );
				return new Dictionary<int, SkillData>();
			}

			return _storageProvider.Skills.GetAll();
		}

		public int GetTotalItemCount()
		{
			return _isDataLoaded ? _storageProvider.Items.Count : 0;
		}

		public int GetTotalMonsterCount()
		{
			return _isDataLoaded ? _storageProvider.Monsters.Count : 0;
		}

		public int GetTotalSkillCount()
		{
			return _isDataLoaded ? _storageProvider.Skills.Count : 0;
		}

		public IEnumerable<ItemData> GetItemsByGrade( string grade )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetItemByGrade called but data is not loaded yet" );
				return Enumerable.Empty<ItemData>();
			}

			return _storageProvider.Items.GetAll().Values
				.Where( item => item.Grade.Equals( grade, StringComparison.OrdinalIgnoreCase ) );
		}

		public IEnumerable<MonsterData> GetMonstersByType( string monsterType )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetMonstersByType called but data is not loaded yet" );
				return Enumerable.Empty<MonsterData>();
			}

			return _storageProvider.Monsters.GetAll().Values
				.Where( monster => monster.MonsterType.Equals( monsterType, StringComparison.OrdinalIgnoreCase ) );
		}

		public IEnumerable<SkillData> GetSkillsByType( string skillType )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetSkillsByType called but data is not loaded yet" );
				return Enumerable.Empty<SkillData>();
			}

			return _storageProvider.Skills.GetAll().Values
				.Where( skill => skill.SkillType.Equals( skillType, StringComparison.OrdinalIgnoreCase ) );
		}

		public IEnumerable<SkillData> GetMonsterSkills( int monsterId )
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetMonsterSkills called but data is not loaded yet" );
				return Enumerable.Empty<SkillData>();
			}

			MonsterData? monster = GetMonster(monsterId);
			if(monster == null || monster.Skills == null || monster.Skills.Count == 0)
			{
				return Enumerable.Empty<SkillData>();
			}

			var skills = new List<SkillData>();
			foreach(int skillId in monster.Skills)
			{
				SkillData? skill = GetSkill(skillId);
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
				string itemsFilePath = _gameDataSettings.GetDataFilePath("items");
				string monsterFilePath = _gameDataSettings.GetDataFilePath("monsters");
				string skillsFilePath = _gameDataSettings.GetDataFilePath("skills");

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
				_storageProvider.Items.Update( itemsDict );
				_storageProvider.Monsters.Update( monstersDict );
				_storageProvider.Skills.Update( skillsDict );

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
			IReadOnlyDictionary<int, ItemData> items = _storageProvider.Items.GetAll();
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
			IReadOnlyDictionary<int, MonsterData> monsters = _storageProvider.Monsters.GetAll();
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
						if(!_storageProvider.Skills.ContainsKey( skillId ))
						{
							_logger.LogError( "Monster {MonsterId} references non-existent skill {SkillId}", monster.Id, skillId );
							isValid = false;
							totalErrors++;
						}
					}
				}
			}

			// Skills 검증
			IReadOnlyDictionary<int, SkillData> skills = _storageProvider.Skills.GetAll();
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
	}
}
