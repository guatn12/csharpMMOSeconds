using System.Net;
using System.ComponentModel.DataAnnotations;

namespace Server.Configuration
{
	public class NetworkSettings
	{
		[Required]
		[RegularExpression( @"^((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$|^0\.0\.0\.0$" )]
		public string Host { get; set; } = "0.0.0.0";

		[Required]
		[Range( 1024, 65535, ErrorMessage = "포트는 1024-65535 범위여야 합니다" )]
		public int Port { get; set; } = 7777;

		[Range( 1, 1000, ErrorMessage = "ListenBacklog는 1-1000 범위여야 합니다" )]
		public int ListenBacklog { get; set; } = 100;

		// IP 주소 유효성 검사 메서드
		public bool IsValidHost()
		{
			return IPAddress.TryParse( Host, out _ );
		}
	}
}
