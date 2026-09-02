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
	public class EquipmentCacheService
	{
		private readonly IDbContextFactory<AppDbContext> _contextFactory;
		private readonly IRedisService _redis;
		private readonly ILogger<EquipmentCacheService> _logger;

		private readonly TimeSpan _cacheTTL = TimeSpan.FromMinutes(30);

		public EquipmentCacheService( IDbContextFactory<AppDbContext> contextFactory, IRedisService redis, ILogger<EquipmentCacheService> logger )
		{
			_contextFactory = contextFactory;
			_redis = redis;
			_logger = logger;
		}

		// 장비 조회
		public async Task<EquipmentEntity> GetPlayerEquipmentAsync( long playerId )
		{
			string cacheKey = PlayerPersistenceCacheKeys.Equipment(playerId);

			try
			{
				EquipmentEntity cachedEquipment = await _redis.GetAsync<EquipmentEntity>(cacheKey);
				if(cachedEquipment != null)
				{
					_logger.LogDebug( "장비 캐시 히트: PlayerId={PlayerId}", playerId );
					return cachedEquipment;
				}

				using var context = _contextFactory.CreateDbContext();
				EquipmentEntity equipment = await context.Equipment.FirstOrDefaultAsync(e => e.PlayerId == playerId);
				if(equipment != null)
				{
					await _redis.SetAsync( cacheKey, equipment, _cacheTTL );
					_logger.LogDebug( "장비 DB → Redis: PlayerId={PlayerId}", playerId );
				}

				return equipment;
			}
			catch(Exception ex)
			{
				using var context = _contextFactory.CreateDbContext();
				_logger.LogError( ex, "장비 조회 실패: PlayerId={PlayerId}", playerId );
				return await context.Equipment.FirstOrDefaultAsync( e => e.PlayerId == playerId );
			}
		}
	}
}
