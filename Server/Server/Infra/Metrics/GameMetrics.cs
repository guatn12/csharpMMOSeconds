using Prometheus;

namespace Server.Infra.Metrics
{
	public static class GameMetrics
	{
		// 현재 활성 세션 수
		public static readonly Gauge ActiveSessions = Prometheus.Metrics.CreateGauge(
			"game_active_sessions",
			"Number of active player sessions in the game server."
		);

		// JobQueue 대기 작업 수
		public static readonly Gauge JobQueueLength = Prometheus.Metrics.CreateGauge(
			"game_job_queue_length",
			"Current length of the job processing queue."
		);

		// ======= 프로세스 메트릭 =======

		// Working Set 메모리 사용량 (bytes)
		public static readonly Gauge ProcessWorkingSet = Prometheus.Metrics.CreateGauge(
			"process_working_set_bytes",
			"Amount of physical memory used by the process."
		);

		// GC 메모리 사용량 (bytes)
		public static readonly Gauge GCMemory = Prometheus.Metrics.CreateGauge(
			"dotnet_gc_memory_bytes",
			"GC managed memory in bytes."
		);

		// 스레드 수
		public static readonly Gauge ThreadCount = Prometheus.Metrics.CreateGauge(
			"process_thread_count",
			"Number of threads in the process."
		);

		// ========= Windows 전용 메트릭 =========

		// CPU 사용률 (%)
		public static readonly Gauge CpuUsagePercent = Prometheus.Metrics.CreateGauge(
			"system_cpu_usage_percent",
			"System CPU usage percentage (Windows only)."
		);

		// 사용 가능한 메모리 (bytes)
		public static readonly Gauge MemoryAvailable = Prometheus.Metrics.CreateGauge(
			"system_memory_available_bytes",
			"Available system memory in bytes (Windows only)."
		);

		// ======= 인프라 헬스 체크 메트릭 =======

		// 데이터베이스 연결 상태 (1 = 정상, 0 = 비정상)
		public static readonly Gauge DbHealthy = Prometheus.Metrics.CreateGauge(
			"infra_database_healthy",
			"Indicates if the database connection is healthy (1 = healthy, 0 = unhealthy)."
		);

		// Redis 연결 상태 (1 = 정상, 0 = 비정상)
		public static readonly Gauge RedisHealthy = Prometheus.Metrics.CreateGauge(
			"infra_redis_healthy",
			"Indicates if the Redis connection is healthy (1 = healthy, 0 = unhealthy)."
		);

	}
}
