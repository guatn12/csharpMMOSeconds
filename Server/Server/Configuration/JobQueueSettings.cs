using System;
using System.ComponentModel.DataAnnotations;

namespace Server.Configuration
{
	public class JobQueueSettings
	{
		[Range(1,64, ErrorMessage = "워커 스레드 수는 1-64개여야 합니다")]
		public int WorkerThreadCount { get; set; } = Environment.ProcessorCount;

		[Range( 100, 100000, ErrorMessage = "최대 큐 크기는 100-100000개여야 합니다" )]
		public int MaxQueueSize { get; set; } = 10000;

		[Range( 1, 10000, ErrorMessage = "큐 체크 간격은 1-10000ms여야 합니다" )]
		public int QueueCheckIntervalMs { get; set; } = 100;

		[Range( 1000, 60000, ErrorMessage = "정리 타이머 간격은 1000-60000ms여야 합니다" )]
		public int CleanupTimerIntervalMs { get; set; } = 5000;

		public bool EnablePriorityQueue { get; set; } = false;

		public bool EnableJobStatistics { get; set; } = true;

		[Range( 1, 3600, ErrorMessage = "셧다운 타임아웃은 1-3600초여야 합니다" )]
		public int ShutdownTimeoutSeconds { get; set; } = 30;
	}
}
