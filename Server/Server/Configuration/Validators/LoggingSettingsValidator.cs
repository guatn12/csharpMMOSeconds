using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Server.Configuration.Validators
{
	public class LoggingSettingsValidator : IValidateOptions<LoggingSettings>
	{
		public ValidateOptionsResult Validate(string name, LoggingSettings options)
		{
			List<string> failures = new List<string>();

			// 로그 레벨 검증
			if (!Enum.IsDefined(typeof(LogLevel), options.Level))
			{
				failures.Add( $"LogLevel '{options.Level}'는 유효하지 않습니다." );
			}

			// 출력 경로 검증
			if(options.EnableFileLogging)
			{
				if(string.IsNullOrWhiteSpace(options.OutputPath))
				{
					failures.Add( "파일 로깅이 활성화된 경우 OutputPath는 필수입니다." );
				}
				else
				{
					try
					{
						string directory = Path.GetDirectoryName(options.OutputPath);
						if(!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
						{
							Directory.CreateDirectory( directory );
						}
					}
					catch(Exception ex)
					{
						failures.Add( $"OutputPath '{options.OutputPath}' 디렉토리 생성 실패: {ex.Message}" );
					}
				}
			}

			// 포맷 문자열 검증
			if (string.IsNullOrWhiteSpace(options.Format))
			{
				failures.Add( "로그 Format은 필수 값입니다." );
			}
			else if(!IsValidLogFormat(options.Format))
			{
				failures.Add( "로그 Format 형식이 올바르지 않습니다." );
			}

			// 파일 크기 및 개수 검증
			if (options.MaxLogFiles < 1 || 100 < options.MaxLogFiles)
			{
				failures.Add( $"MaxLogFiles {options.MaxLogFiles}는 1-100 범위여야 합니다." );
			}

			if (options.MaxLogFileSizeMB < 1 || 1000 < options.MaxLogFileSizeMB)
			{
				failures.Add( $"MaxLogFileSizeMB {options.MaxLogFileSizeMB}는 1-1000 범위여야 합니다." );
			}

			// 최소 하나의 로깅 출력은 활성화되어야 함.
			if(!options.EnableConsoleLogging && !options.EnableConsoleLogging)
			{
				failures.Add( "콘솔 또는 파일 로깅 중 최소 하나는 활성화되어야 합니다." );
			}

			return 0 < failures.Count
				? ValidateOptionsResult.Fail( failures )
				: ValidateOptionsResult.Success;
		}

		private bool IsValidLogFormat(string format)
		{
			string[] requiredPlacecholders = new[] {"Timestamp", "{Level", "{Message" };
			return requiredPlacecholders.All(placeholder => format.Contains(placeholder));
		}
	}
}
