using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Server.Configuration.Validators
{
	public class JobQueueSettingsValidator : IValidateOptions<JobQueueSettings>
	{
		public ValidateOptionsResult Validate( string name, JobQueueSettings options )
		{
			List<string> failures = new List<string>();

			// 워커 스레드 수 검증
			int processCount = Environment.ProcessorCount;
			if(options.WorkerThreadCount < 1)
			{
				failures.Add( "WorkerThreadCount는 최소 1개 이상이어야 합니다." );
			}
			else if( 4 * processCount < options.WorkerThreadCount)
			{
				failures.Add($"WorkerThreadCount {options.WorkerThreadCount}는 CPU 코어 수의 4배({processCount * 4})를 초과할 수 없습니다.");
			}

			// 큐 크기 검증
			if(options.MaxQueueSize < 100)
			{
				failures.Add( "MaxQueueSize는 최소 100개 이상이어야 합니다." );
			}
			else if(100000 < options.MaxQueueSize)
			{
				failures.Add( "MaxQueueSize는 최대 100,000개까지 허용됩니다." );
			}

			// 메모리 사용량 추정 검증
			int estimatedMemoryMB = (options.MaxQueueSize * 1024) / (1024*1024); // 1KB per job 가정
			if(100 < estimatedMemoryMB )
			{
				failures.Add( $"MaxQueueSize가 너무 큽니다. 예상 메모리 사용량: {estimatedMemoryMB}" );
			}

			// 체크 간격 검증
			if (options.QueueCheckIntervalMs < 1 || 10000 < options.QueueCheckIntervalMs)
			{
				failures.Add( $"QueueCheckIntervalMs {options.QueueCheckIntervalMs}는 1-10000 범위여야 합니다." );
			}

			// 정리 타이머 간격 검증
			if (options.CleanupTimerIntervalMs < 1000 || 60000 < options.CleanupTimerIntervalMs)
			{
				failures.Add( $"CleanupTimerIntervalMs {options.CleanupTimerIntervalMs}는 1000-60000 범위여야 합니다." );
			}

			// 셧다운 타임아웃 검증
			if(options.ShutdownTimeoutSeconds < 1 || 3600 < options.ShutdownTimeoutSeconds)
			{
				failures.Add( $"ShutdownTimeoutSeconds {options.ShutdownTimeoutSeconds}는 1-3600 범위여야 합니다." );
			}

			// 논리적 일관성 검증
			if( options.CleanupTimerIntervalMs < options.QueueCheckIntervalMs)
			{
				failures.Add( "QueueCheckIntervalMs는 CleanupTimerIntervalMs보다 작아야 합니다." );
			}

			return 0 < failures.Count
				? ValidateOptionsResult.Fail(failures)
				: ValidateOptionsResult.Success;
		}
	}
}
