using DatabaseLib.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace DatabaseLib.Redis
{
	public class RedisService : IDisposable, IRedisService
	{
		private readonly IConnectionMultiplexer _connectionMultiplexer;
		private readonly IDatabase _database;
		private readonly RedisOptions _redisOption;
		private readonly ILogger<RedisService> _logger;
		private readonly string _keyPrefix = "MMO:";
		
		private readonly object _lock  = new object();

		public bool IsConnected => _connectionMultiplexer.IsConnected;

		public RedisService(IConnectionMultiplexer connectionMultiplexer, 
			IOptions<RedisOptions> redisOptions, ILogger<RedisService> logger)
		{
			_connectionMultiplexer = connectionMultiplexer;
			_redisOption = redisOptions.Value;
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
			string serializedValue = JsonSerializer.Serialize(value);
			string redisKey = GetKey(key);
			TimeSpan actualExpiry = expiry ?? TimeSpan.FromHours(1);

			bool success = await _database.StringSetAsync(redisKey, serializedValue, actualExpiry);
			if(!success)
			{
				_logger.LogDebug( "Redis Set 실패: Key={Key}", key );
				return false;	
			}

			_logger.LogDebug( "Redis Set 성공: Key={Key}, Expiry={Expiry}", key, actualExpiry );
			return success;
		}

		public async Task<T> GetAsync<T>(string key ) where T : class
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

		public async Task<String> GetStringAsync(string key)
		{
			string redisKey = GetKey(key);
			var value = await _database.StringGetAsync(redisKey);

			return value.HasValue ? value.ToString() : null;
		}

		public async Task<bool> DeleteAsync(string key)
		{
			string redisKey = GetKey(key);
			bool success = await _database.KeyDeleteAsync(redisKey);

			_logger.LogDebug( "Redis Delete: Key={Key}, Result={Result}", key, success );
			return success;
		}

		public async Task HashSetAsync(string key, IReadOnlyDictionary<string, string> fields, TimeSpan? expiry = null)
		{
			string redisKey = GetKey(key);
			HashEntry[] entries = fields.Select(kv => new HashEntry(kv.Key, kv.Value)).ToArray();

			await _database.HashSetAsync( redisKey, entries );
			if(expiry.HasValue)
				await _database.KeyExpireAsync( redisKey, expiry.Value );
		}

		public async Task<T> HashGetAsync<T>(string key, string fieldKey) where T : class
		{
			string redisKey = GetKey(key);
			RedisValue value = await _database.HashGetAsync(redisKey, fieldKey);
			if(!value.HasValue)
			{
				_logger.LogDebug( "Redis HashGet - 키/필드 없음: Key={Key}/Field={Field}", key, fieldKey );
				return null;
			}

			var deserializedValue = JsonSerializer.Deserialize<T>(value);
			_logger.LogDebug( "Redis HashGet 성공: Key={Key} / fieldKey={fieldKey}", key, fieldKey );
			return deserializedValue;
		}

		public async Task<IReadOnlyDictionary<string, string>> HashGetAllAsync(string key)
		{
			HashEntry[] entries = await _database.HashGetAllAsync(GetKey(key));
			// HashEntry[] -> dict. 키 없으면 빈 dict
			return entries.ToDictionary( e => e.Name.ToString(), e => e.Value.ToString() );
		}

		public async Task SetAddAsync(string key, string member)
		{
			string redisKey = GetKey(key);
			await _database.SetAddAsync( redisKey, member );
		}

		public async Task SetRemoveAsync(string key, string member)
		{
			string redisKey = GetKey(key);
			await _database.SetRemoveAsync( redisKey, member );
		}

		public async Task<IReadOnlyCollection<string>> SetMembersAsync(string key)
		{
			string redisKey = GetKey(key);
			RedisValue[] members = await _database.SetMembersAsync(redisKey);
			return members.Select( m => m.ToString() ).ToList();
		}

		public Task<bool> KeyExistsAsync( string key ) => _database.KeyExistsAsync( GetKey( key ) );

		public Task<bool> KeyExpireAsync( string key, TimeSpan expiry ) => _database.KeyExpireAsync( GetKey( key ), expiry );

		public IRedisBatch CreateBatch() => new RedisBatch( _database.CreateBatch(), GetKey, _logger );

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
