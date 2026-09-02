using DatabaseLib.Entities;
using DatabaseLib.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseLib.Services
{
	public class PlayerStateCacheService
	{
		private readonly IDbContextFactory<AppDbContext> _contextFactory;
		private readonly IRedisService _redis;
		private readonly ILogger<PlayerStateCacheService> _logger;
		private readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(30);

		public PlayerStateCacheService(IDbContextFactory<AppDbContext> contextFactory, IRedisService redis, 
			ILogger<PlayerStateCacheService> logger)
		{
			_contextFactory = contextFactory;
			_redis = redis;
			_logger = logger;
		}

		public async Task<PlayerStateEntity> GetPlayerStateAsync(long playerId)
		{
			string cacheKey = PlayerPersistenceCacheKeys.State(playerId);

			try
			{
				PlayerStateEntity cached = await _redis.GetAsync<PlayerStateEntity>(cacheKey);
				if(cached != null)
					return cached;

				using var context = _contextFactory.CreateDbContext();
				PlayerStateEntity entity = await context.PlayerState.AsNoTracking()
					.SingleOrDefaultAsync(x => x.PlayerId == playerId);

				if(entity != null)
					await _redis.SetAsync( cacheKey, entity, _cacheTtl );

				return entity;
			}
			catch (Exception ex)
			{
				_logger.LogError( ex, "PlayerState 캐시 조회 실패: PlayerId={PlayerId}", playerId );
				using var context = _contextFactory.CreateDbContext();
				return await context.PlayerState.AsNoTracking()
					.SingleOrDefaultAsync( x => x.PlayerId == playerId );
			}
		}
	}
}
