using DatabaseLib.Redis;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthServer.HealthChecks
{
	public class AuthRedisHealthChecks : IHealthCheck
	{
		private readonly IRedisService _redisService;

		public AuthRedisHealthChecks( IRedisService redisService )
		{
			_redisService = redisService;
		}

		public async Task<HealthCheckResult> CheckHealthAsync( HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken() )
		{
			cancellationToken.ThrowIfCancellationRequested();

			bool pingSucceeded = await _redisService.PingAsync();
			return pingSucceeded
				? HealthCheckResult.Healthy( "Redis ping succeeded." )
				: HealthCheckResult.Unhealthy( "Redis ping failed." );
		}
	}
}
