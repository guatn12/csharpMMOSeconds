using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Config;
using Server.Data.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Server.Data
{
	public class DataManager
	{
		private readonly ILogger<DataManager> _logger;
		private readonly ServerSettings _serverSettings;

		private readonly ConcurrentDictionary<int, ItemData> _items = new ConcurrentDictionary<int, ItemData>();
		private readonly ConcurrentDictionary<int, MonsterData> _monsters = new ConcurrentDictionary<int, MonsterData>();
		private readonly ConcurrentDictionary<int, SkillData> _skills = new ConcurrentDictionary<int, SkillData>();
		private readonly ConcurrentDictionary<int, MapData> _maps = new ConcurrentDictionary<int, MapData>();
		private GameConfigData _gameConfig;

		private bool _isDataLoaded = false;
		private DateTime _lastLoadTime = DateTime.MinValue;
		private readonly object _lock = new object();

		public GameConfigData GameConfig => _gameConfig;

		public DataManager(
			IOptions<ServerSettings> serverOptions,
			ILogger<DataManager> logger )
		{
			_logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
			_serverSettings = serverOptions.Value;
			_logger.LogInformation( "DataManager initialized with data path: {DataPath}", _serverSettings.GameData.DataPath );
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

		public MapData? GetMap(int mapId)
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetMap called but data is not loaded yet" );
				return null;
			}

			return _maps.TryGetValue( mapId, out var map ) ? map : null;
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

		public IReadOnlyDictionary<int, MapData> GetAllMaps()
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetAllMaps called but data is not loaded yet" );
				return new Dictionary<int, MapData>();
			}
			return _maps;
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

		public int GetTotalMapCount()
		{
			return _isDataLoaded ? _maps.Count : 0;
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
				string itemsFilePath = _serverSettings.GameData.GetDataFilePath("items");
				string monsterFilePath = _serverSettings.GameData.GetDataFilePath("monsters");
				string skillsFilePath = _serverSettings.GameData.GetDataFilePath("skills");
				string mapsFilePath = _serverSettings.GameData.GetDataFilePath("maps");
				string gameConfigFilePath = _serverSettings.GameData.GetDataFilePath("gameconfig");

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

				if(!File.Exists( mapsFilePath ))
				{
					_logger.LogError( "Maps data file not found: {FilePath}", mapsFilePath );
					return false;
				}

				if(!File.Exists( gameConfigFilePath ))
				{
					_logger.LogError( "GameConfig data file not found: {FilePath}", gameConfigFilePath );
					return false;
				}

				//json 파일 로드
				string itemsJson = await File.ReadAllTextAsync(itemsFilePath);
				string monstersJson = await File.ReadAllTextAsync(monsterFilePath);
				string skillsJson = await File.ReadAllTextAsync(skillsFilePath);
				string mapsJson = await File.ReadAllTextAsync(mapsFilePath);
				string gameConfigJson = await File.ReadAllTextAsync(gameConfigFilePath);

				// json 역직렬화
				List<ItemData> items = JsonSerializer.Deserialize<List<ItemData>>(itemsJson);
				List<MonsterData> monsters = JsonSerializer.Deserialize<List<MonsterData>>(monstersJson);
				List<SkillData> skills = JsonSerializer.Deserialize<List<SkillData>>(skillsJson);
				List<MapData> maps = JsonSerializer.Deserialize<List<MapData>>(mapsJson);
				_gameConfig = JsonSerializer.Deserialize<GameConfigData>( gameConfigJson );

				if(items == null || monsters == null || skills == null || maps == null)
				{
					_logger.LogError( "Filaed to deserialize game data files" );
					return false;
				}

				// 맵 셀 파싱
				foreach(var map in maps)
				{
					map.ParseCells();
				}

				// dictionary 변환
				Dictionary<int, ItemData> itemsDict = items.ToDictionary(item => item.Id);
				Dictionary<int, MonsterData> monstersDict = monsters.ToDictionary(monster => monster.Id);
				Dictionary<int, SkillData> skillsDict = skills.ToDictionary(skill => skill.Id);
				Dictionary<int, MapData> mapsDict = maps.ToDictionary(map => map.Id);

				// 스레드 안전하게 데이터 업데이트
				UpdateData(itemsDict, monstersDict, skillsDict, mapsDict);

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

			// Maps 검증
			IReadOnlyDictionary<int, MapData> maps = _maps;
			List<int> invalidMaps = new List<int>();

			foreach(var kvp in maps)
			{
				var map = kvp.Value;

				// 기본 검증
				if(!map.IsValid())
				{
					invalidMaps.Add( map.Id );
					_logger.LogError( "Invalid map data: ID={MapId}, Name={Name}", map.Id, map.Name );
					isValid = false;
					totalErrors++;
				}

				// ID 일치성 검증(Dictionary Key와 실제 ID가 같은지)
				if(kvp.Key != map.Id)
				{
					_logger.LogError( "Map ID mismatch: Key={Key}, ActualId={ActualId}", kvp.Key, map.Id );
					isValid = false;
					totalErrors++;
				}
			}

			// GameConfig 검증
			isValid = isValid && _gameConfig.IsValid();

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
			Dictionary<int, SkillData> skillDict,
			Dictionary<int, MapData> mapDict)
		{
			_items.Clear();
			_monsters.Clear();
			_skills.Clear();
			_maps.Clear();

			foreach(var kvp in itemDict) _items[ kvp.Key ] = kvp.Value;
			foreach(var kvp in monsterDict) _monsters[ kvp.Key ] = kvp.Value;
			foreach(var kvp in skillDict) _skills[ kvp.Key ] = kvp.Value;
			foreach(var kvp in mapDict) _maps[ kvp.Key ] = kvp.Value;
		}
	}
}
