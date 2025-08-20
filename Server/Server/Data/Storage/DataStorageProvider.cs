using Server.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data.Storage
{
	/// <summary>
	/// 모든 데이터 관리하는 중앙 제공자
	/// </summary>
	public class DataStorageProvider : IDataStorageProvider
	{
		public IThreadSafeDataStorage<int, ItemData> Items { get; }
		public IThreadSafeDataStorage<int, MonsterData> Monsters { get; }
		public IThreadSafeDataStorage<int, SkillData> Skills { get; }

		public DataStorageProvider()
		{
			Items = new ConCurrentDataStorage<int, ItemData>();
			Monsters = new ConCurrentDataStorage<int, MonsterData>();
			Skills = new ConCurrentDataStorage<int, SkillData>();
		}
	}
}
