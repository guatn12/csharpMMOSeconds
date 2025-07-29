using System.ComponentModel.DataAnnotations;

namespace Server.Configuration
{
	public class SecuritySettings
	{
		[Required(ErrorMessage = "EncryptionKey는 필수입니다")]
		[StringLength( 256, MinimumLength = 32, ErrorMessage = "암호화 키는 32-256자여야 합니다" )]
		public string EncryptionKey { get; set; } = string.Empty;

		[Required(ErrorMessage = "TokenSecret는 필수입니다")]
		[StringLength( 512, MinimumLength = 64, ErrorMessage = "토큰 시크릿은 64-512자여야 합니다" )]
		public string TokenSecret { get; set; } = string.Empty;

		[Range( 1, 7200, ErrorMessage = "토큰 만료 시간은 1-7200분여야 합니다" )]
		public int TokenExpirationMinutes { get; set; } = 60;

		public bool EnableEncryption { get; set; } = false;

		public bool EnableRateLimiting { get; set; } = true;

		[Range( 1, 10000, ErrorMessage = "분당 최대 요청수는 1-10000개여야 합니다" )]
		public int MaxRequestsPerMinute { get; set; } = 100;

		public ApiKeysSettings ApiKeys { get; set; } = new();
		public ExternalServicesSettings ExternalServices { get; set; } = new();
	}

	public class ApiKeysSettings
	{
		public string RedisApiKey { get; set; } = string.Empty;
		public string MonitoringApiKey {  get; set; } = string.Empty;
		public string LoggingServiceApiKey { get; set; } = string.Empty;
		public string MetricsApiKey {  get; set; } = string.Empty;
	}

	public class ExternalServicesSettings
	{
		public string AuthServiceUrl { get; set; } = string.Empty;
		public string AuthServiceApiKey {  get; set; } = string.Empty;
		public string PaymentServiceUrl {  get; set; } = string.Empty;
		public string PaymentServiceApiKey { get; set;} = string.Empty;
	}
}
