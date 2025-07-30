using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data.Storage
{
	public interface IThreadSafeDataStorage
	{
		bool ReplaceItems( Dictionary<string, object> newData );
		bool ReplaceMonsters( Dictionary<string, object> newData );
		bool ReplaceSkills( Dictionary<string, object> newData );
		int GetRecordCount( string tableName );
	}
}
