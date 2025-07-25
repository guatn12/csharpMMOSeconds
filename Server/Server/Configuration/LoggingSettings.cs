using System.ComponentModel.DataAnnotations;

namespace Server.Configuration
{
	public enum LogLevel
	{
		Trace, Debug, Information, Warning, Error, Critical
	}

	public class LoggingSettings
	{
		[Required]
		public LogLevel Level { get; set; } = LogLevel.Information;

		public string Format { get; set; } = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

		public string OutputPath { get; set; } = "logs/server-.log";

		public bool EnableConsoleLogging { get; set; } = true;

		public bool EnableFileLogging { get; set; } = true;

		[Range( 1, 100, ErrorMessage = "로그 파일 최대 개수는 1-100개여야 합니다" )]
		public int MaxLogFiles { get; set; } = 30;

		[Range( 1, 1000, ErrorMessage = "로그 파일 최대 크기는 1-1000MB여야 합니다" )]
		public int MaxLogFileSizeMB { get; set; } = 50;
	}
}
