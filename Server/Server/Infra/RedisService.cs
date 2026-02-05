using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Config;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Server.Infra
{
	public class RedisService : IDisposable
	{
		private readonly IConnectionMultiplexer _connectionMultiplexer;
		private readonly IDatabase _database;
		private readonly RedisConfig _redisConfig;
		private readonly ILogger<RedisService> _logger;
		private readonly string _keyPrefix = "MMO:";
		
		private readonly object _lock  = new object();

		public RedisService(IConnectionMultiplexer connectionMultiplexer, 
			IOptions<ServerSettings> serverSettings, ILogger<RedisService> logger)
		{
			_connectionMultiplexer = connectionMultiplexer;
			_redisConfig = serverSettings.Value.Redis;
			_logger = logger;

			_database = _connectionMultiplexer.GetDatabase();

			_logger.LogInformation( "RedisService 초기화 완료. KeyPrefix: {KyePrefix}", _keyPrefix );
		}

		private string GetKey(string key) => $"{_keyPrefix}{key}";

		public async Task<bool> PingAsync()
		{
			try
			{
				var pingTime = await _database.PingAsync();
				_logger.LogDebug( "Redis Ping 성공: {PingTime}ms", pingTime.TotalMilliseconds );
				return true;
			}
			catch ( Exception ex )
			{
				_logger.LogError( ex, "Redis Ping 실패" );
				return false;
			}
		}

		public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
		{
			try
			{
				string serializedValue = JsonSerializer.Serialize(value);
				string redisKey = GetKey(key);
				TimeSpan actualExpiry = expiry ?? TimeSpan.FromHours(1);

				bool success = await _database.StringSetAsync(redisKey, serializedValue, actualExpiry);
				if( success )
				{
					_logger.LogDebug( "Redis Set 성공: Key={Key}, Expiry={Expiry}", key, actualExpiry );
				}
				else
				{
					_logger.LogDebug( "Redis Set 실패: Key={Key}", key );
				}

				return success;
			}
			catch(Exception ex )
			{
				_logger.LogError( ex, "Redis Set 오류 : Key={Key}", key );
				return false;
			}
		}

		public async Task<T> GetAsync<T>(string key ) where T : class
		{
			try
			{
				string redisKey = GetKey(key);
				var value = await _database.StringGetAsync(redisKey);
				if(!value.HasValue)
				{
					_logger.LogDebug( "Redis Get - 키 없음: Key={Key}", key );
					return null;
				}

				var deserializedValue = JsonSerializer.Deserialize<T>(value);
				_logger.LogDebug( "Redis Get 성공: Key={Key}", key );
				return deserializedValue;
			}
			catch (Exception ex)
			{
				_logger.LogError( ex, "Redis Get 오류: Key={Key}", key );
				return null;
			}
		}

		public async Task<String> GetStringAsync(string key)
		{
			try
			{
				string redisKey = GetKey(key);
				var value = await _database.StringGetAsync(redisKey);

				return value.HasValue ? value.ToString() : null;
			}
			catch (Exception ex)
			{
				_logger.LogError( ex, "Redis GetString 오류: Key={Key}", key );
				return null;
			}
		}

		public async Task<bool> DeleteAsync(string key)
		{
			try
			{
				string redisKey = GetKey(key);
				bool success = await _database.KeyDeleteAsync(redisKey);

				_logger.LogDebug( "Redis Delete: Key={Key}, Result={Result}", key, success );
				return success;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Redis Delete 오류: Key={Key}", key );
				return false;
			}
		}

		// 세션 관리 헬퍼 메서드
		public async Task<bool> SetSessionAsync(long sessionId, object sessionData, TimeSpan? expiry = null)
		{
			return await SetAsync( $"session:{sessionId}", sessionData, expiry );
		}

		public async Task<T> GetSessionAsync<T>(long sessionId) where T : class
		{
			return await GetAsync<T>( $"session:{sessionId}" );
		}

		public async Task<bool> DeleteSessionAsync(long sessionId)
		{
			return await DeleteAsync( $"session:{sessionId}" );
		}

		// 플레이어 상태 헬퍼 메서드
		public async Task<bool> SetPlayerStateAsync(long playerId, object playerData, TimeSpan? expriy = null)
		{
			return await SetAsync($"player:{playerId}", playerData, expriy );
		}

		public async Task<T> GetPlayerStateAsync<T>(long playerId) where T : class
		{
			return await GetAsync<T>( $"player:{playerId}" );
		}

		public async Task<bool> DeletePlayerStateAsync(long playerId)
		{
			return await DeleteAsync( $"player:{playerId}" );
		}
		public void Dispose()
		{
			try
			{
				_connectionMultiplexer.Dispose();
				_logger.LogInformation( "RedisService 리소스 정리 완료" );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "RedisService Dispose 오류" );
			}
		}
	}
}
