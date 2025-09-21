using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Extensions
{
	public static class RedisExtensions
	{
		/// <summary>
		/// HashEntry 배열에서 float 값을 추출
		/// </summary>
		public static float GetFloat(this HashEntry[] entries, string key, float defaultValue = 0f)
		{
			HashEntry entry = entries.FirstOrDefault(x => x.Name == key);
			return entry.Value.TryParse(out double result) ? (float)result : defaultValue;
		}

		/// <summary>
		/// HasEntry 배열에서 double 값을 안전하게 추출
		/// </summary>
		public static double GetDouble(this HashEntry[] entries, string key, double defaultValue = 0f)
		{
			HashEntry entry = entries.FirstOrDefault(x => x.Name == key);
			return entry.Value.TryParse(out double result) ? result : defaultValue;
		}

		public static long GetLong(this HashEntry[] entries, string key, long defulatValue = 0L)
		{
			HashEntry entry = entries.FirstOrDefault(x => x.Name == key);
			return entry.Value.TryParse(out long result) ? result : defulatValue;
		}

		public static int GetInt(this HashEntry[] entries, string key, int defaultValue = 0)
		{
			HashEntry entry = entries.FirstOrDefault(x=> x.Name == key);
			return entry.Value.TryParse(out int result) ? result : defaultValue;
		}

		public static string GetString(this HashEntry[] entries, string key, string defaultValue = "")
		{
			HashEntry entry = entries.FirstOrDefault(x => x.Name == key);
			return entry.Value.IsNull? defaultValue : entry.Value.ToString();
		}

		public static bool GetBool( this HashEntry[] entries, string key, bool defaultValue = false )
		{
			HashEntry entry = entries.FirstOrDefault( x => x.Name == key);

			// bool 파싱 : "true"/"false" 문자열 또는 "1"/"0" 숫자
			if(entry.Value.IsNull) return defaultValue;

			string value = entry.Value.ToString().ToLowerInvariant();

			return value switch
			{
				"true" or "1" => true,
				"false" or "0" => false,
				_ => defaultValue
			};
		}


	}
}
