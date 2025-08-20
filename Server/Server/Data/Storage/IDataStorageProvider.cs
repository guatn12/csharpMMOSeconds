using Server.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data.Storage
{
	/// <summary>
	/// 모든 데이터 스토리지에 대한 중앙 접근 인터페이스
	/// </summary>
	public interface IDataStorageProvider
	{
		IThreadSafeDataStorage<int, ItemData> Items { get; }
		IThreadSafeDataStorage<int, MonsterData> Monsters { get; }
		IThreadSafeDataStorage<int, SkillData> Skills { get; }
	}
}
