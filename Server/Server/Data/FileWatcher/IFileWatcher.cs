using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Data.FileWatcher
{
	public interface IFileWatcher : IDisposable
	{
		event EventHandler<FileChangedEventArgs> FileChanged;
		void StartWatching();
		void StopWatching();
	}

	public class FileChangedEventArgs : EventArgs
	{
		public string FilePath { get; set; }
		public string TableName { get; set; }
		public DateTime ChangeTime { get; set; }
	}
}
