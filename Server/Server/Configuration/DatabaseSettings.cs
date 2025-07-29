using System.ComponentModel.DataAnnotations;

namespace Server.Configuration
{
	public class DatabaseSettings
	{
		[Required(ErrorMessage = "ConnectionString은 필수입니다")]
		public string ConnectionString { get; set; } = string.Empty;

		public string RedisConnectionString { get; set; } = string.Empty;

		[Range( 1, 1000, ErrorMessage = "최대 연결 풀 크기는 1-1000개여야 합니다" )]
		public int MaxPoolSize { get; set; } = 100;

		[Range( 0, 100, ErrorMessage = "최소 연결 풀 크기는 0-100개여야 합니다" )]
		public int MinPoolSize { get; set; } = 5;

		[Range( 1, 300, ErrorMessage = "연결 타임아웃은 1-300초여야 합니다" )]
		public int ConnectionTimeoutSeconds { get; set; } = 30;

		[Range( 1, 3600, ErrorMessage = "명령 타임아웃은 1-300초여야 합니다" )]
		public int CommandTimeoutSeconds { get; set; } = 30;

		public bool EnableConnectionPooling { get; set; } = true;

		public bool EnableRetryOnFailure { get; set; } = true;

		[Range( 1, 10, ErrorMessage = "재시도 횟수는 1-10회여야 합니다" )]
		public int MaxRetryCount { get; set; } = 3;
	}
}
