using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DatabaseLib.Redis
{
	public class RedisBatch : IRedisBatch
	{
		private readonly IBatch _batch;
		private readonly Func<string, string> _prefix;
		private readonly ILogger _logger;
		private readonly List<(string desc, Task task)> _pending = new();		// (설명, task) 쌍으로 보관 -> 실패 시 어느 op 인지 추적

		public RedisBatch(IBatch batch, Func<string, string> prefix, ILogger logger)
		{
			_batch = batch;
			_prefix = prefix;
			_logger = logger;
		}

		async Task IRedisBatch.ExecuteAsync()
		{
			_batch.Execute();		// 큐에 쌓인 명령을 한 번에 전송(동기 dispatch)
			try
			{
				await Task.WhenAll( _pending.Select( p => p.task ) );
			}
			catch
			{
				// 어느 op(키)가 실패했는지 키 레벨로 로깅 후 rethrow (추적성)
				foreach (var (desc, task) in _pending)
				{
					if(task.IsFaulted)
						_logger.LogError( task.Exception, "Redis batch op faulted: {Op}", desc );
				}
				throw;
			}
		}

		Task<IReadOnlyDictionary<string, string>> IRedisBatch.HashGetAllAsync( string key )
		{
			// Execute 전엔 미완료 task -> 내부에서 await 금지. raw를 _pending에 넣고 미완료 task를 감싼 wrapper 반환
			// (async 래퍼는 첫 await에서 즉시 양보 -> 블로킹 없음).
			string k = _prefix(key);
			Task<HashEntry[]> raw = _batch.HashGetAllAsync(k);
			_pending.Add( ($"HashGetAll {k}", raw) );
			return ToDictAsync( raw );

			static async Task<IReadOnlyDictionary<string, string>> ToDictAsync( Task<HashEntry[]> r )
				=> (await r).ToDictionary( e => e.Name.ToString(), e => e.Value.ToString() );
		}

		void IRedisBatch.HashSet( string key, IReadOnlyDictionary<string, string> fields )
		{
			HashEntry[] entries = fields.Select(kv => new HashEntry(kv.Key, kv.Value)).ToArray();
			string k = _prefix(key);
			_pending.Add( ($"HashSet {k}", _batch.HashSetAsync( k, entries )) );
		}

		void IRedisBatch.KeyDelete( string key )
		{
			string k = _prefix(key);
			_pending.Add( ($"KeyDelete {k}", _batch.KeyDeleteAsync( k )) );
		}

		void IRedisBatch.KeyExpire( string key, TimeSpan expiry )
		{
			string k = _prefix(key);
			_pending.Add( ($"KeyExpire {k}", _batch.KeyExpireAsync( k, expiry )) );
		}

		void IRedisBatch.SetAdd( string key, string member )
		{
			string k = _prefix(key);
			_pending.Add( ($"SetAdd {k}", _batch.SetAddAsync( k, member )) );
		}

		void IRedisBatch.SetRemove( string key, string member )
		{
			string k = _prefix(key);
			_pending.Add( ($"SetRemove {k}", _batch.SetRemoveAsync( k, member )) );
		}
	}
}
