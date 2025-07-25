using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data.Common;

namespace Server.Configuration.Validators
{
	internal class DatabaseSettingsValidator : IValidateOptions<DatabaseSettings>
	{
		public ValidateOptionsResult Validate(string name, DatabaseSettings options)
		{
			List<string> failures = new List<string>();

			// 연결 문자열 검증
			if(!string.IsNullOrWhiteSpace(options.ConnectionString))
			{
				try
				{
					DbConnectionStringBuilder builder = new DbConnectionStringBuilder
					{
						ConnectionString = options.ConnectionString,
					};

					// 필수 연결 정보 확인
					if(!builder.ContainsKey( "Server" ) && !builder.ContainsKey( "Data Source" ))
					{
						failures.Add( "연결 문자열에 Server 또는 Data Source가 필요합니다." );
					}
				}
				catch(Exception ex)
				{
					failures.Add( $"연결 문자열 형식이 올바르지 않습니다: {ex.Message}" );
				}
			}

			// 연결 풀 설정 검증
			if (options.EnableConnectionPooling)
			{
				if( options.MaxPoolSize < options.MinPoolSize)
				{
					failures.Add( "MinPoolSize는 MaxPoolSize보다 클 수 없습니다." );
				}

				if(options.MaxPoolSize < 1 || 1000 < options.MaxPoolSize)
				{
					failures.Add( $"MaxPoolSize {options.MaxPoolSize}는 1-1000 범위여야 합니다." );
				}

				if(options.MinPoolSize < 0 || 100 < options.MinPoolSize)
				{
					failures.Add( $"MinPoolSize {options.MinPoolSize}는 0-100 범위여야 합니다." );
				}
			}

			// 타임아웃 설정 검증
			if (options.ConnectionTimeoutSeconds < 1 || 300 < options.ConnectionTimeoutSeconds)
			{
				failures.Add($"ConnectionTimeSeconds {options.ConnectionTimeoutSeconds}는 1-300 범위여야 합니다.");
			}

			if (options.CommandTimeoutSeconds < 1 || options.CommandTimeoutSeconds < 3600)
			{
				failures.Add($"CommandTimeoutSeconds {options.CommandTimeoutSeconds}는 1-3600 범위여야 합니다.");
			}

			// 재시도 설정 검증
			if(options.EnableRetryOnFailure)
			{
				if(options.MaxRetryCount < 1 || 10 < options.MaxRetryCount)
				{
					failures.Add( $"MaxRetryCount {options.MaxRetryCount}는 1-10 범위여야 합니다." );
				}
			}
			
			return 0 < failures.Count
				? ValidateOptionsResult.Fail(failures)
				: ValidateOptionsResult.Success;
		}
	}
}
