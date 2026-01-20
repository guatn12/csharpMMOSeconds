using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Infra.HealthCheck
{
	public class RedisHealthCheck : IHealthCheck
	{
		private readonly IConnectionMultiplexer _redis;

		// 생성자에서 이미 만들어진 진짜 Redis 연결을 주입받음
		public RedisHealthCheck( IConnectionMultiplexer redis )
		{
			_redis = redis;
		}

		public Task<HealthCheckResult> CheckHealthAsync( HealthCheckContext context,
			CancellationToken cancellationToken = default )
		{
			if(_redis.IsConnected)
				return Task.FromResult( HealthCheckResult.Healthy( "Redis Connected" ) );

			return Task.FromResult( HealthCheckResult.Unhealthy( "Redis Connected fail" ) );
		}
	}
}
