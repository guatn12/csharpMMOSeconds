using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Concurrent;

namespace ServerCore
{
    public class JobPool
    {
        private readonly DefaultObjectPoolProvider _provider;
        private readonly ConcurrentDictionary<Type, object> _pools = new ConcurrentDictionary<Type, object>();
		private readonly ConcurrentDictionary<Type, Action<IJob>> _returnActions = new ConcurrentDictionary<Type, Action<IJob>>();

        public JobPool()
        {
            _provider = new DefaultObjectPoolProvider();
        }

        public T Get<T>() where T : class, IJob, new()
        {
            var pool = GetOrCreatePool<T>();
            return pool.Get();
        }

        public void Return<T>(T item) where T : class, IJob, new()
        {
            var pool = GetOrCreatePool<T>();
            pool.Return( item );
        }

		public void Return(IJob item)
		{
			if(item == null) 
				return;

			if(_returnActions.TryGetValue( item.GetType(), out var returnAction ))
			{
				returnAction( item );
			}
		}

        private ObjectPool<T> GetOrCreatePool<T>() where T : class, IJob, new()
        {
            return (ObjectPool<T>)_pools.GetOrAdd( typeof( T ), type =>
			{
				var pool = _provider.Create( new DefaultPooledObjectPolicy<T>() );
				_returnActions.TryAdd( type, ( job ) => pool.Return( (T)job ) );

				return pool;
			} );
        }
    }
}