using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseLib.Redis
{
	public interface IRedisService
	{
		bool IsConnected { get; }
		IRedisBatch CreateBatch();

		Task<bool> PingAsync();
		Task<bool> SetAsync<T>( string key, T value, TimeSpan? expiry = null );
		Task<T> GetAsync<T>( string key ) where T : class;
		Task<String> GetStringAsync( string key );
		Task<bool> DeleteAsync( string key );
		Task HashSetAsync( string key, IReadOnlyDictionary<string, string> fields, TimeSpan? expiry = null );
		Task<IReadOnlyDictionary<string, string>> HashGetAllAsync( string key );
		Task SetAddAsync( string key, string member );
		Task SetRemoveAsync( string key, string member );
		Task<IReadOnlyCollection<string>> SetMembersAsync( string key );
		Task<bool> KeyExistsAsync( string key );
	}
}
