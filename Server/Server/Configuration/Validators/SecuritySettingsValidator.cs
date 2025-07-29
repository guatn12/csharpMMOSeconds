using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Server.Configuration.Validators
{
	public class SecuritySettingsValidator : IValidateOptions<SecuritySettings>
	{
		public ValidateOptionsResult Validate( string name, SecuritySettings options )
		{
			List<string> failures = new List<string>();

			// 암호화 활성화 시 필수 키 검증
			if(string.IsNullOrWhiteSpace( options.EncryptionKey ))
			{
				failures.Add( "암호화가 활성화된 경우 EncryptionKey는 필수입니다." );
			}
			else if(options.EncryptionKey.Length < 32)
			{
				failures.Add( "EncryptionKey는 최소 32자 이상이어야 합니다." );
			}
			else if(!IsStrongKey( options.EncryptionKey ))
			{
				failures.Add( "EncryptionKey는 대소문자, 숫자, 특수문자를 포함해야 합니다." );
			}

			// 토큰 시크릿 검증
			if(!string.IsNullOrWhiteSpace( options.TokenSecret ))
			{
				if(options.TokenSecret.Length < 32)
				{
					failures.Add( "TokenSecret은 최소 32자 이상이어야 합니다." );
				}
				else if(!IsStrongKey(options.TokenSecret))
				{
					failures.Add( "TokenSecret은 대소문자, 숫자, 특수문자를 포함해야 합니다." );
				}
			}

			// 토큰 만료 시간 검증
			if(options.TokenExpirationMinutes < 1 || 7200 < options.TokenExpirationMinutes)
			{
				failures.Add( $"TokenExpirationMinutes {options.TokenExpirationMinutes}는 1-7200 범위여야 합니다." );
			}

			// Rate Limiting 설정 검증
			if(options.EnableRateLimiting)
			{
				if(options.MaxRequestsPerMinute < 1 || 10000 < options.MaxRequestsPerMinute)
				{
					failures.Add( $"MaxRequestsPerMinute {options.MaxRequestsPerMinute}는 1-10000 범위여야 합니다." );
				}
			}
			
			return 0 < failures.Count
				? ValidateOptionsResult.Fail( failures )
				: ValidateOptionsResult.Success;
		}

		private bool IsStrongKey(string key)
		{
			if(string.IsNullOrWhiteSpace( key ) || key.Length < 32) return false;

			bool hasUpper = key.Any(char.IsUpper);
			bool hasLower = key.Any(char.IsLower);
			bool hasDigit = key.Any(char.IsDigit);
			bool hasSpecial = key.Any(c => !char.IsLetterOrDigit(c));

			return hasUpper && hasLower && hasDigit && hasSpecial;
		}
	}
}
