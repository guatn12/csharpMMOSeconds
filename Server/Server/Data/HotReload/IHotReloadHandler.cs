using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data.HotReload
{
	public interface IHotReloadHandler
	{
		Task<bool> ReloadDataAsync( string tableName, string filePath );
		event EventHandler<DataReloadedEventArgs> DataReloaded;
	}

	public class DataReloadedEventArgs : EventArgs
	{
		public string TableName { get; set; }
		public int RecordCount { get; set; }
		public bool Success { get; set; }
		public string ErrorMessage { get; set; }
		public DateTime ReloadTime { get; set; }
	}
}
