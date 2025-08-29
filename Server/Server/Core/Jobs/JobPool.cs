using Microsoft.Extensions.ObjectPool;
using ServerCore;
using System;
using System.Collections.Concurrent;

namespace Server.Core.Jobs
{
    public class JobPool
    {
        private readonly DefaultObjectPoolProvider _provider;
        private readonly ConcurrentDictionary<Type, object> _pools = new ConcurrentDictionary<Type, object>();

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

        private ObjectPool<T> GetOrCreatePool<T>() where T : class, IJob, new()
        {
            return (ObjectPool<T>)_pools.GetOrAdd( typeof( T ), _ => _provider.Create( new DefaultPooledObjectPolicy<T>() ) );
        }
    }
}