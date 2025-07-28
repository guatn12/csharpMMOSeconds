using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Configuration.Security
{
	/// <summary>
	/// 보안 관련 유틸리티 클래스
	/// </summary>
	public static class SecurityHelper
	{
		private static readonly HashSet<string> SensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"password", "secret", "key", "token", "connectionstring",
			"encryptionkey", "tokensecret", "apikey", "clientsecret"
		};

		/// <summary>
		/// 민감한 값을 마스킹 처리
		/// </summary>
		/// <param name="value"></param>
		/// <param name="visibleChars"></param>
		/// <returns></returns>
		public static string MaskSensitiveValue(string value, int visibleChars = 2)
		{
			if(string.IsNullOrEmpty( value ))
				return "[빈값]";

			if(value.Length <= visibleChars)
				return new string( '*', value.Length );

			return value.Substring( 0, visibleChars ) + new string( '*', Math.Max( 4, value.Length - visibleChars ) );
		}

		/// <summary>
		/// 키 이름이 민감한 정보인지 확인.
		/// </summary>
		/// <param name="keyName"></param>
		/// <returns></returns>
		public static bool IsSensitive( string keyName )
		{
			if (string.IsNullOrEmpty( keyName )) return false;

			return SensitiveKeys.Any( sensitive => keyName.Contains( sensitive, StringComparison.OrdinalIgnoreCase ) );
		}

		/// <summary>
		/// 환경변수에서 값 가져오기 (fallback) 지원
		/// </summary>
		/// <param name="envVarName"></param>
		/// <param name="fallbackValue"></param>
		/// <returns></returns>
		public static string GetSecureValue(string envVarName, string fallbackValue = "")
		{
			var envValue = Environment.GetEnvironmentVariable(envVarName);
			return !string.IsNullOrEmpty( envValue ) ? envValue : fallbackValue;
		}

		/// <summary>
		/// 설정 객체의 민감한 정보를 마스킹하여 로깅용 문자열 생성
		/// </summary>
		/// <param name="configObject"></param>
		/// <param name="objectName"></param>
		/// <returns></returns>
		public static string CreateSafeLogString(object configObject, string objectName = "Configuration")
		{
			// 간단한 reflection 기반 마스킹 (실제로드 JSON 직렬화 후 처리)
			return $"{objectName}: [민감정보 마스킹됨]";
		}
	}
}
