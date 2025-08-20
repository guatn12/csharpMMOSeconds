using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Server.Configuration;
using Server.Data.Models;
using Server.Data.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data
{
	public class DataManager : IDataManager
	{
		private readonly IDataStorageProvider _storageProvider;
		private readonly IConfiguration _configuration;
		private readonly ILogger<DataManager> _logger;
		private readonly GameDataSettings _gameDataSettings;

		private bool _isDataLoaded = false;
		private DateTime _lastLoadTime = DateTime.MinValue;
		private readonly object _lock = new object();

		public DataManager(
			IDataStorageProvider storageProvider,
			IConfiguration configuration,
			ILogger<DataManager> logger )
		{
			_storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
			_configuration = configuration ?? throw new ArgumentNullException( nameof(configuration));
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));

			_gameDataSettings = _configuration.GetSection( "GameData" ).Get<GameDataSettings>() ?? new GameDataSettings();

			_logger.LogInformation( "DataManager initialized with data path: {DataPath}", _gameDataSettings.DataPath );
		}

		public ItemData? GetItem(int itemId)
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetItem called but data is not loaded yet" );
				return null;
			}

			return _storageProvider.Items.Get( itemId );
		}

		public MonsterData? GetMonster(int monsterId)
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetMonster called but data is not loaded yet" );
				return null;
			}

			return _storageProvider.Monsters.Get( monsterId );
		}

		public SkillData? GetSkill(int skillId)
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

		public IEnumerable<ItemData> GetItemsByGrade(string grade)
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetItemByGrade called but data is not loaded yet" );
				return Enumerable.Empty<ItemData>();
			}

			return _storageProvider.Items.GetAll().Values
				.Where( item => item.Grade.Equals( grade, StringComparison.OrdinalIgnoreCase ) );
		}

		public IEnumerable<MonsterData> GetMonstersByType(string monsterType)
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetMonstersByType called but data is not loaded yet" );
				return Enumerable.Empty<MonsterData>();
			}

			return _storageProvider.Monsters.GetAll().Values
				.Where( monster => monster.MonsterType.Equals( monsterType, StringComparison.OrdinalIgnoreCase ) );
		}

		public IEnumerable<SkillData> GetSkillByType(string skillType)
		{
			if(!_isDataLoaded)
			{
				_logger.LogWarning( "GetSkillByType called but data is not loaded yet" );
				return Enumerable.Empty<SkillData>();
			}

			return _storageProvider.Skills.GetAll().Values
				.Where( skill => skill.SkillType.Equals( skillType, StringComparison.OrdinalIgnoreCase ) );
		}

		public IEnumerable<SkillData> GetMonsterSkills(int monsterId)
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
					skills.Add(skill);
				}
			}

			return skills;
		}

		public bool IsDataLoaded => _isDataLoaded;
		public DateTime LastLoadTime => _lastLoadTime;

		public async Task<bool> LoadAllDataAsync()
		{
			// TODO : 구현 필요
			throw new NotImplementedException();
		}

		public bool ValidateAllData()
		{
			// TODO : 구현 필요
			throw new NotImplementedException(  );
		}
	}
}
