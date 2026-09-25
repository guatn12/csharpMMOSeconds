using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prometheus;
using Server.Config;
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
		private readonly HealthCheckService _healthCheckService;
		private readonly MetricsConfig _settings;

		private readonly ISessionManager _sessionManager;
		private readonly IJobQueueManager _jobQueueManager;
		private MetricServer _metricServer;

		private TimeSpan _previousTotalProcessorTime;
		private long _previousTImestamp;
		private bool _hasPreviousCpuSample;

		private readonly int _processorCount = Math.Max( 1, Environment.ProcessorCount );

		public PerformanceMonitoringService(ILogger<PerformanceMonitoringService> logger, ISessionManager sessionManager,
			IJobQueueManager jobQueueManager, HealthCheckService healthCheckService, IOptions<ServerSettings> settings)
		{
			_logger = logger;
			_sessionManager = sessionManager;
			_jobQueueManager = jobQueueManager;
			_healthCheckService = healthCheckService;
			_settings = settings.Value.Metrics;
		}

		protected override async Task ExecuteAsync( CancellationToken stoppingToken )
		{
			// Metric Server 시작
			_metricServer = new MetricServer(hostname: _settings.Host, port: _settings.Port );
			_metricServer.Start();
			_logger.LogInformation( "Prometheus Metric Server started on port {Port}", _settings.Port );

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
			long currentTimestamp = Stopwatch.GetTimestamp();
			
			using var process = Process.GetCurrentProcess();

			TimeSpan currentTotalProcessorTime = process.TotalProcessorTime;
			long workingSetBytes = process.WorkingSet64;
			long gcMemoryBytes = GC.GetTotalMemory(false);
			int threadCount = process.Threads.Count;

			// Prometheus Gauge 업데이트
			GameMetrics.ActiveSessions.Set(_sessionManager.GetAllActiveSessions().Count());
			//GameMetrics.JobQueueLength.Set(_jobQueueManager.GetJobQueueLength());

			// 프로세스 메트릭
			GameMetrics.ProcessWorkingSet.Set( workingSetBytes );
			GameMetrics.GCMemory.Set(gcMemoryBytes);
			GameMetrics.ProcessThreadCount.Set(threadCount);

			bool hasCpuUsage = TryCalculateCpuUsagePercent( currentTotalProcessorTime, currentTimestamp,
				out double cpuUsagePercent );
			if(hasCpuUsage)
			{
				GameMetrics.ProcessCpuUsagePercent.Set( cpuUsagePercent );

				_logger.LogInformation( "Process CPU Usage: {CpuUsage:F1}%", cpuUsagePercent );
				if(90.0 < cpuUsagePercent)
				{
					_logger.LogWarning( "High process CPU usage: {CpuUsage:F1}%", cpuUsagePercent );
				}
			}

			// 로깅 (MB 단위)
			long workingSetMB = workingSetBytes / (1024 * 1024);
			long gcMemoryMB = gcMemoryBytes / (1024 * 1024);
			
			// 기본 성능 메트릭 (모든 플랫폼)
			_logger.LogInformation( "Performance Metrics - Working Set: {WorkingSet}MB, GC Memory: {GcMemory}MB, Threads: {ThreadCount}",
				workingSetMB, gcMemoryMB, threadCount );

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

		private bool TryCalculateCpuUsagePercent( TimeSpan currentTotalProcessorTime, long currentTimestamp,
			out double cpuUsagePercent )
		{
			cpuUsagePercent = 0;

			// 첫 표본은 비교 대상이 없으므로 기준값만 저장한다.
			if(!_hasPreviousCpuSample)
			{
				_previousTotalProcessorTime = currentTotalProcessorTime;
				_previousTImestamp = currentTimestamp;
				_hasPreviousCpuSample = true;

				return false;
			}

			TimeSpan processorTimeDelta = currentTotalProcessorTime - _previousTotalProcessorTime;

			TimeSpan elapsedTime = Stopwatch.GetElapsedTime( _previousTImestamp, currentTimestamp );

			// 다음 수집을 위해 현재 값을 이전 표본으로 저장한다.
			_previousTotalProcessorTime = currentTotalProcessorTime;
			_previousTImestamp = currentTimestamp;

			// 정상적으로는 발생하지 않지만 잘못된 표본으로 나누는 것을 방지한다.
			if(elapsedTime <= TimeSpan.Zero || processorTimeDelta < TimeSpan.Zero)
				return false;

			double availableProcessorTime = elapsedTime.TotalSeconds * _processorCount;

			double calculatedUsage = processorTimeDelta.TotalSeconds / availableProcessorTime * 100.0;
			cpuUsagePercent = Math.Clamp( calculatedUsage, 0.0, 100.0 );

			return true;
		}
	}
}
