using Microsoft.Extensions.Logging;
using Server.Data.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Data.HotReload
{
	public class HotReloadHandler : IHotReloadHandler
	{
		private readonly IThreadSafeDataStorage _storage;
		private readonly ILogger<HotReloadHandler> _logger;
		private readonly SemaphoreSlim _reloadSemaphore = new SemaphoreSlim(1,1);

		public event EventHandler<DataReloadedEventArgs> DataReloaded;

		public HotReloadHandler(
			IThreadSafeDataStorage storage,
			ILogger<HotReloadHandler> logger )
		{
			_storage = storage;
			_logger = logger;
		}

		public async Task<bool> ReloadDataAsync( string tableName, string filePath )
		{
			// 동시 리로드 방지
			await _reloadSemaphore.WaitAsync();

			try
			{
				_logger.LogInformation( "Starting hot reload for table: {TableName}", tableName );

				// 파일 존재 확인
				if(!File.Exists( filePath ))
				{
					var error = $"Data file not found: {filePath}";
					_logger.LogError( error );
					OnDataReloaded( tableName, 0, false, error );
					return false;
				}

				// 파일이 사용 중 일 수 있으므로 재시도 로직
				var content = await ReadFileWithRetryAsync(filePath);
				if(content == null )
				{
					var error = "Failed to read file after retries";
					_logger.LogError( error );
					OnDataReloaded( tableName, 0, false, error );
					return false;
				}

				// 테이블별 리로드 처리
				var success = tableName.ToLower() switch
				{
					"items"=>await ReloadItemsAsync(content),
					"monsters"=>await ReloadMonstersAsync(content),
					"skills"=>await ReloadSkillsAsync(content),
					_ => false
				};

				if (success)
				{
					var count = _storage.GetRecordCount(tableName);
					_logger.LogInformation( "Hot reload completed for {TableName}: {Count} records", tableName, count );
					OnDataReloaded( tableName, count, true, null );
				}

				return success;
			}
			catch ( Exception ex )
			{
				_logger.LogError( ex, "Hot reload failed for table: {TableName}", tableName );
				OnDataReloaded( tableName, 0, false, ex.Message );
				return false;
			}
			finally
			{
				_reloadSemaphore.Release();
			}
		}

		private async Task<string> ReadFileWithRetryAsync(string filePath, int maxRetries = 3)
		{
			for (int i = 0; i < maxRetries; i++)
			{
				try
				{
					// 파일이 다른 프로세스에서 사용 중일 수 있음.
					using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
					using var reader = new StreamReader(stream);
					return await reader.ReadToEndAsync();
				}
				catch (IOException) when (i < maxRetries - 1)
				{
					await Task.Delay( 100 *(i+1) ); // 점진적 지연
				}
			}

			return null;
		}

		private async Task<bool> ReloadItemsAsync(string jsonContent)
		{
			try
			{
				var items = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
				return _storage.ReplaceItems( items );
			}
			catch(JsonException ex)
			{
				_logger.LogError( ex, "Invalid JSON format in Items data" );
				return false;
			}
		}

		private async Task<bool> ReloadMonstersAsync( string jsonContent )
		{
			try
			{
				var monsters = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
				return _storage.ReplaceItems( monsters );
			}
			catch(JsonException ex)
			{
				_logger.LogError( ex, "Invalid JSON format in Monsters data" );
				return false;
			}
		}

		private async Task<bool> ReloadSkillsAsync( string jsonContent )
		{
			try
			{
				var skills = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
				return _storage.ReplaceItems( skills );
			}
			catch(JsonException ex)
			{
				_logger.LogError( ex, "Invalid JSON format in Skills data" );
				return false;
			}
		}

		private void OnDataReloaded( string tableName, int recordCount, bool success, string errorMessage )
		{
			DataReloaded?.Invoke( this, new DataReloadedEventArgs
			{
				TableName = tableName,
				RecordCount = recordCount,
				Success = success,
				ErrorMessage = errorMessage,
				ReloadTime = DateTime.UtcNow
			} );
		}
	}
}
