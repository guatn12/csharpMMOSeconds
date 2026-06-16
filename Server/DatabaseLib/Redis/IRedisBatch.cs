using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseLib.Redis
{
	public interface IRedisBatch
	{
		void HashSet( string key, IReadOnlyDictionary<string, string> fields );
		void KeyExpire( string key, TimeSpan expiry );
		void SetAdd( string key, string member );
		void SetRemove( string key, string member );
		void KeyDelete( string key );
		/// <summary> 반환 Task를 ExecuteAsync() 호출 전에 await하지 마세요 — 데드락. ExecuteAsync() 후 await.</summary>
		Task<IReadOnlyDictionary<string, string>> HashGetAllAsync( string key );
		Task ExecuteAsync();
	}
}
