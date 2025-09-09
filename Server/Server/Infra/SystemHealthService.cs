using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Infra
{
	public class SystemHealthService
	{
		private readonly ILogger<SystemHealthService> _logger;
		private readonly AppDbContext _dbContext;
		private readonly IConnectionMultiplexer _redis;

		public SystemHealthService(
			ILogger<SystemHealthService> logger,
			AppDbContext dbContext,
			IConnectionMultiplexer redis)
		{
			_logger = logger;
			_dbContext = dbContext;
			_redis = redis;
		}

		public async Task<bool> CheckSystemHealthAsync()
		{
			bool overallHealth = true;

			// 1. Database 연결 체크
			bool dbHealth = await CheckDatabaseHealthAsync();
			if(!dbHealth) overallHealth = false;

			// 2. Redis 연결 체크
			bool redisHealth = CheckRedisHealth();
			if(!redisHealth) overallHealth = false;

			// 3. 메모리 사앹 체크
			bool memoryHealth = CheckMemoryHealth();
			if(!memoryHealth) overallHealth = false;

			_logger.LogInformation( "System Health Check Complete - Overall: {OverallHealth}, DB: {DbHealth}, Redis: {RedisHealth}," +
				"Memory: {MemoryHealth}",
				overallHealth ? "Healthy" : "Unhealthy",
				dbHealth ? "OK" : "FAIL",
				redisHealth ? "OK" : "FAIL",
				memoryHealth ? "OK" : "FAIL" );

			return overallHealth;
			
		}

		private async Task<bool> CheckDatabaseHealthAsync()
		{
			// 간단한 DB 연결 테스트
			await _dbContext.Database.ExecuteSqlRawAsync( "SELECT 1" );
			_logger.LogDebug( "Database health check: OK" );
			return true;
		}

		private bool CheckRedisHealth()
		{
			if(_redis.IsConnected)
			{
				// Redis ping 테스트
				IDatabase database = _redis.GetDatabase();
				TimeSpan pingResult = database.Ping();
				_logger.LogDebug( "Redis health check: OK (Ping: {PingTime}ms", pingResult.TotalMilliseconds );
				return true;
			}
			else
			{
				_logger.LogWarning( "Redis health check: Connection not established" );
				return false;
			}
		}

		private bool CheckMemoryHealth()
		{
			long workingSetMB = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
			long gcMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);

			// 메모리 사용량이 1GB를 초과하면 경고
			if(1024 < workingSetMB)
			{
				_logger.LogWarning( "High memory usage detected: Working Set {WorkingSet}MB, GC Memory {GcMemory}MB",
					workingSetMB, gcMemoryMB );
				return false;
			}

			_logger.LogDebug( "Memory health check: OK (Working Set: {WorkingSet}MB, Gc: {GcMemory}MB)", workingSetMB, gcMemoryMB );
			return true;
		}
	}
}
