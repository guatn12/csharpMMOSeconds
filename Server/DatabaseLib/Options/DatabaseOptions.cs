using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseLib.Options
{
	public sealed class DatabaseOptions
	{
		[Required]
		public string ConnectionString { get; set; }

		// Connection Pool 설정
		public int MinPoolSize { get; set; } = 10;                  // 기본 연결 유지
		public int MaxPoolSize { get; set; } = 100;                 // 동접 1000명 기준 10:1 비율
		public int ConnectionTimeout { get; set; } = 30;            // 연결 타임아웃 (초)
		public int CommandTimeout { get; set; } = 15;               // 쿼리 타임아웃 (초)
		public int MaxRetryOnFailure { get; set; } = 3;             // 재시도 횟수
		public bool EnableRetryOnFailure { get; set; } = true;
		public bool EnableSensitiveDataLogging { get; set; } = false;
	}
}
