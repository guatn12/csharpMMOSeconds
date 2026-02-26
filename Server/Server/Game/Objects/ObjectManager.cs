using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game.Objects
{
	public class ObjectManager
	{
		private readonly ConcurrentDictionary<long, GameObject> _objects = new ConcurrentDictionary<long, GameObject>();
		private readonly ILogger _logger;

		public ObjectManager(ILogger logger)
		{ 
			_logger = logger; 
		}

		// 등록 / 해제
		public bool Register(GameObject obj)
		{
			if(_objects.TryAdd(obj.ObjectId, obj) == false)
			{
				_logger.LogWarning("GameObject {ObjectId} TryAdd Fail", obj.ObjectId);
				return false;
			}

			return true;
		}

		public bool Unregister(long objectId)
		{
			if(_objects.TryRemove(objectId, out GameObject obj) == false)
			{
				_logger.LogWarning( "GameObject {ObjectId} TryRemove Fail", objectId );
				return false;
			}

			return true;
		}

		// 조회
		public GameObject GetObject(long objectId)
		{
			if(_objects.TryGetValue(objectId, out GameObject obj) == false)
			{
				_logger.LogWarning( "GameObject {ObjectId} Not Found", objectId );
				return null;
			}

			return obj;
		}

		public T GetObject<T>(long objectId) where T : GameObject
		{
			var obj = GetObject(objectId);
			if(obj == null)
			{
				return null;
			}

			return obj as T;
		}	
	}
}
