using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Infra
{
	public class PerformanceMonitoringService : IDisposable
	{
		private readonly ILogger<PerformanceMonitoringService> _logger;
		private readonly Timer _monitoringTimer;
		private readonly bool _isWindows;
		private PerformanceCounter _cpuCounter;
		private PerformanceCounter _memoryCounter;

		public PerformanceMonitoringService(ILogger<PerformanceMonitoringService> logger)
		{
			_logger = logger;
			_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

			// Windows 전용 카운터 초기화
			if(_isWindows )
			{
				try
				{
					_cpuCounter = new PerformanceCounter( "Processor", "% Processor Time", "_Total" );
					_memoryCounter = new PerformanceCounter( "Memory", "Available MBytes" );
				}
				catch(Exception ex)
				{
					_logger.LogWarning( ex, "Windows performance counters not available" );
				}
			}

			// 30초마다 성능 메트릭 로깅
			_monitoringTimer = new Timer( LogPerformanceMetrics, null, TimeSpan.Zero, TimeSpan.FromSeconds( 30 ) );
		}

		private void LogPerformanceMetrics(object state)
		{
			Process process = Process.GetCurrentProcess();
			long workingSetMB = process.WorkingSet64 / (1024 * 1024);
			long gcMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
			int threadCount = process.Threads.Count;

			// 기본 성능 메트릭 (모든 플랫폼)
			_logger.LogInformation("Performance Metrics - Working Set: {WorkingSet}MB, GC Memory: {GcMemory}MB, Threads: {ThreadCount}",
				workingSetMB, gcMemoryMB, threadCount);

			// Windows 전용 카운터
			if(_isWindows && _cpuCounter != null && _memoryCounter != null)
			{
				float cpuUsage = _cpuCounter.NextValue();
				float availableMemoryMB = _memoryCounter.NextValue();

				_logger.LogInformation( "Windows Metrics - CPU: {CpuUsage:F1}%, Available Memory: {AvailableMemory}MB",
					cpuUsage, availableMemoryMB );

				// 경고 임계값 체크
				if(90 < cpuUsage)
					_logger.LogWarning( "High CPU usage warning: {CpuUsage:F1}%", cpuUsage );

				if(500 < availableMemoryMB)
					_logger.LogWarning( "Low memory warning: {AvailableMemory}MB available", availableMemoryMB );
			}

			// 메모리 사용량
			if(500 < workingSetMB)
				_logger.LogWarning( "High memory usage warning: Working set is {WorkingSet}MB", workingSetMB );
		}

		public void Dispose()
		{
			_monitoringTimer.Dispose();
			_cpuCounter.Dispose();
			_memoryCounter.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
