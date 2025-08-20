using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Data.FileWatcher
{
	public class GameDataFileWatcher : IFileWatcher
	{
		private readonly GameDataSettings _settings;
		private readonly ILogger<GameDataFileWatcher> _logger;
		private FileSystemWatcher _watcher;
		private readonly Timer _debounceTimer;
		private readonly Dictionary<string, DateTime> _pendingChanges = new();
		private readonly object _lock = new object();

		public event EventHandler<FileChangedEventArgs> FileChanged;

		public GameDataFileWatcher(
			IOptions<GameDataSettings> options,
			ILogger<GameDataFileWatcher> logger)
		{
			_settings = options.Value;
			_logger = logger;

			_debounceTimer = new Timer( ProcessPendingChanges, null, Timeout.Infinite, Timeout.Infinite );
		}

		public void StartWatching()
		{
			// 개발 환경이고, Hot Reload가 활성화된 경우만
			var environmnetName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")??"Production";
			bool isDevelopment = environmnetName.Equals("Development", StringComparison.OrdinalIgnoreCase);
			if(!_settings.EnableHotReload || !isDevelopment)
			{
				_logger.LogInformation( "File watching disabled" );
				return;
			}

			// 디렉토리 존재 확인
			if(!Directory.Exists(_settings.DataPath))
			{
				_logger.LogWarning("GameData directory not found: {Path}", _settings.DataPath);
				return;
			}

			// FileSystemWatcher 설정
			_watcher = new FileSystemWatcher( _settings.DataPath, $"*{_settings.FileExtension}" )
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
				EnableRaisingEvents = true
			};

			_watcher.Changed += OnFileChanged;
			_watcher.Created += OnFileChanged;

			_logger.LogInformation( "File watching started on: {Path}", _settings.DataPath );
		}

		private void OnFileChanged(object sender, FileSystemEventArgs e)
		{
			lock (_lock)
			{
				_pendingChanges[ e.FullPath ] = DateTime.UtcNow;
			}

			// Debounce 타이머 재시작
			_debounceTimer?.Change( _settings.HotReloadDebounceMs, Timeout.Infinite );
		}

		private void ProcessPendingChanges(object state)
		{
			Dictionary<string, DateTime> changesToProcess;

			lock(_lock)
			{
				changesToProcess = new Dictionary<string, DateTime>( _pendingChanges );
				_pendingChanges.Clear();
			}

			foreach(var change in changesToProcess)
			{
				var tableName = Path.GetFileNameWithoutExtension(change.Key);
				FileChanged?.Invoke( this, new FileChangedEventArgs
				{
					FilePath = change.Key,
					TableName = tableName,
					ChangeTime = change.Value
				} );
			}
		}

		public void StopWatching()
		{
			_watcher?.Dispose();
			_debounceTimer?.Dispose();
		}

		public void Dispose()
		{
			StopWatching();
		}
	}
}
