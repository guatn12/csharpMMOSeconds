using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Server.Data.Storage
{
	public class ConCurrentDataStorage<TKey, TValue> : IThreadSafeDataStorage<TKey, TValue> where TKey : notnull
	{
		private ConcurrentDictionary<TKey, TValue> _data = new();

		public int Count => _data.Count;

		public bool ContainsKey( TKey key ) => _data.ContainsKey( key );

		public TValue? Get( TKey key )
		{
			return _data.TryGetValue( key, out var value ) ? value : default;
		}

		public IReadOnlyDictionary<TKey, TValue> GetAll()
		{
			return _data.ToImmutableDictionary();
		}

		public void Update( Dictionary<TKey, TValue> newData )
		{
			// 원자적 교체 : 새로운 ConcurrentDictionary로 교체
			var newConcurrentDict = new ConcurrentDictionary<TKey, TValue>(newData);

			// InterLock.Exchange로 스레드 안전하게 교체
			Interlocked.Exchange(ref _data, newConcurrentDict);
		}
	}
}
