using Microsoft.Extensions.Logging;
using Server.Data.Models;
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
		private readonly IDataStorageProvider _storageProvider;
		private readonly ILogger<HotReloadHandler> _logger;
		private readonly SemaphoreSlim _reloadSemaphore = new SemaphoreSlim(1,1);

		public event EventHandler<DataReloadedEventArgs> DataReloaded;

		public HotReloadHandler(
			IDataStorageProvider storageProvider,
			ILogger<HotReloadHandler> logger )
		{
			_storageProvider = storageProvider;
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
				if(content == null)
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

				if(success)
				{
					var count = tableName.ToLower() switch
					{
						"items"=>_storageProvider.Items.Count,
						"monsters"=>_storageProvider.Monsters.Count,
						"skills"=>_storageProvider.Skills.Count,
						_ => 0
					};
					_logger.LogInformation( "Hot reload completed for {TableName}: {Count} records", tableName, count );
					OnDataReloaded( tableName, count, true, null );
				}

				return success;
			}
			catch(Exception ex)
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

		private async Task<string> ReadFileWithRetryAsync( string filePath, int maxRetries = 3 )
		{
			for(int i = 0; i < maxRetries; i++)
			{
				try
				{
					// 파일이 다른 프로세스에서 사용 중일 수 있음.
					using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
					using var reader = new StreamReader(stream);
					return await reader.ReadToEndAsync();
				}
				catch(IOException) when(i < maxRetries - 1)
				{
					await Task.Delay( 100 *(i+1) ); // 점진적 지연
				}
			}

			return null;
		}

		private async Task<bool> ReloadItemsAsync( string jsonContent )
		{
			try
			{
				var items = JsonSerializer.Deserialize<ItemData[]>(jsonContent);
				if(items == null) return false;

				var itemDict = items.ToDictionary(item => item.Id);
				_storageProvider.Items.Update( itemDict );

				_logger.LogInformation("Items reloaded: {Count} items", itemDict.Count);
				
				return true;
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
				var monsters = JsonSerializer.Deserialize<MonsterData[]>(jsonContent);
				if(monsters == null) return false;

				var monsterDict = monsters.ToDictionary(monster => monster.Id);
				_storageProvider.Monsters.Update( monsterDict );

				_logger.LogInformation( "Monsters reloaded: {Count} monsters", monsterDict.Count );

				return true;
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
				var skills = JsonSerializer.Deserialize<SkillData[]>(jsonContent);
				if(skills == null) return false;

				var skilldict = skills.ToDictionary(skill => skill.Id);
				_storageProvider.Skills.Update( skilldict );

				_logger.LogInformation( "Skills reloaded: {Count} skills", skilldict.Count );

				return true;
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
