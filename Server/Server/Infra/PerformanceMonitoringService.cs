using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using Server.Core.Session;
using Server.Infra.Metrics;
using ServerCore;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Infra
{
	public class PerformanceMonitoringService : BackgroundService
	{
		private readonly ILogger<PerformanceMonitoringService> _logger;
		private readonly bool _isWindows;
		private readonly HealthCheckService _healthCheckService;
		private PerformanceCounter _cpuCounter;
		private PerformanceCounter _memoryCounter;

		private readonly ISessionManager _sessionManager;
		private readonly IJobQueueManager _jobQueueManager;
		private MetricServer _metricServer;
		private readonly int _port = 9100;

		public PerformanceMonitoringService(ILogger<PerformanceMonitoringService> logger, ISessionManager sessionManager,
			IJobQueueManager jobQueueManager, HealthCheckService healthCheckService)
		{
			_logger = logger;
			_sessionManager = sessionManager;
			_jobQueueManager = jobQueueManager;
			_healthCheckService = healthCheckService;
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
		}

		protected override async Task ExecuteAsync( CancellationToken stoppingToken )
		{
			// Metric Server 시작
			_metricServer = new MetricServer(hostname: "localhost", port: _port );
			_metricServer.Start();
			_logger.LogInformation( "Prometheus Metric Server started on port {Port}", _port );

			// PeriodcTimer 생성 (30초 간격)
			using var timer = new PeriodicTimer( TimeSpan.FromSeconds( 30 ) );

			// 시작 시 즉시 한 번 실행
			CollectMetrics();
			await CollectHealthCheckAsync();

			try
			{
				while(await timer.WaitForNextTickAsync( stoppingToken ))
				{
					CollectMetrics();

					await CollectHealthCheckAsync();
				}
			}
			catch(OperationCanceledException)
			{
				// 서비스 중지 시 예외 무시
			}
		}

		public override Task StopAsync( CancellationToken cancellationToken )
		{
			_metricServer.Stop();
			_logger.LogInformation( "Prometheus Metric Server stopped" );

			return base.StopAsync( cancellationToken );
		}

		private void CollectMetrics()
		{
			Process process = Process.GetCurrentProcess();
			long workingSetBytes = process.WorkingSet64;
			long gcMemoryBytes = GC.GetTotalMemory(false);
			int threadCount = process.Threads.Count;

			// Prometheus Gauge 업데이트
			GameMetrics.ActiveSessions.Set(_sessionManager.GetAllActiveSessions().Count());
			//GameMetrics.JobQueueLength.Set(_jobQueueManager.GetJobQueueLength());

			// 프로세스 메트릭
			GameMetrics.ProcessWorkingSet.Set( workingSetBytes );
			GameMetrics.GCMemory.Set(gcMemoryBytes);
			GameMetrics.ThreadCount.Set(threadCount);

			// 로깅 (MB 단위)
			long workingSetMB = workingSetBytes / (1024 * 1024);
			long gcMemoryMB = gcMemoryBytes / (1024 * 1024);

			// 기본 성능 메트릭 (모든 플랫폼)
			_logger.LogInformation( "Performance Metrics - Working Set: {WorkingSet}MB, GC Memory: {GcMemory}MB, Threads: {ThreadCount}",
				workingSetMB, gcMemoryMB, threadCount );

			// Windows 전용 카운터
			if(_isWindows && _cpuCounter != null && _memoryCounter != null)
			{
				float cpuUsage = _cpuCounter.NextValue();
				float availableMemoryMB = _memoryCounter.NextValue();

				// Prometheus Gauge 업데이트
				GameMetrics.CpuUsagePercent.Set(cpuUsage);
				GameMetrics.MemoryAvailable.Set(availableMemoryMB * 1024 * 1024); // bytes 단위로 변환

				_logger.LogInformation( "Windows Metrics - CPU: {CpuUsage:F1}%, Available Memory: {AvailableMemory}MB",
					cpuUsage, availableMemoryMB );

				// 경고 임계값 체크
				if(90 < cpuUsage)
					_logger.LogWarning( "High CPU usage warning: {CpuUsage:F1}%", cpuUsage );

				if(availableMemoryMB < 500)
					_logger.LogWarning( "Low memory warning: {AvailableMemory}MB available", availableMemoryMB );
			}

			// 메모리 사용량
			if(500 < workingSetMB)
				_logger.LogWarning( "High memory usage warning: Working set is {WorkingSet}MB", workingSetMB );
		}

		private async Task CollectHealthCheckAsync()
		{
			var report = await _healthCheckService.CheckHealthAsync();
			// 데이터베이스 헬스 체크 결과 반영
			var dbEntry = report.Entries.FirstOrDefault(e => e.Key.Contains("database"));
			if(dbEntry.Key != null)
			{
				int dbHealthy = dbEntry.Value.Status == HealthStatus.Healthy ? 1 : 0;
				GameMetrics.DbHealthy.Set(dbHealthy);
				_logger.LogInformation( "Database Health Check - Status: {Status}", dbEntry.Value.Status );
			}

			var redisEntry = report.Entries.FirstOrDefault(e => e.Key.Contains("redis"));
			if(redisEntry.Key != null)
			{
				int redisHealthy = redisEntry.Value.Status == HealthStatus.Healthy ? 1 : 0;
				GameMetrics.RedisHealthy.Set(redisHealthy);
				_logger.LogInformation( "Redis Health Check - Status: {Status}", redisEntry.Value.Status );
			}
		}

		public override void Dispose()
		{
			_cpuCounter?.Dispose();
			_memoryCounter?.Dispose();
			base.Dispose();
		}
	}
}
