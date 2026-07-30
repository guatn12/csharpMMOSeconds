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

		private const string EQUIPMENT_CACHE_PREFIX = "equipment:";
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
			string cacheKey = $"{EQUIPMENT_CACHE_PREFIX}{playerId}";

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

		public async Task<bool> SaveEquipmentToDbAsync( EquipmentEntity equipment )
		{
			try
			{
				using var context = _contextFactory.CreateDbContext();
				context.Equipment.Update( equipment );

				equipment.LastUpdated = DateTime.UtcNow;
				equipment.Version++;    // 낙관적 동시성 제어

				await context.SaveChangesAsync();

				return true;
			}
			catch(DbUpdateConcurrencyException ex)
			{
				_logger.LogWarning( ex, "장비 동시성 충돌: PlayerId={PlayerId}", equipment.PlayerId );
				await InvalidateEquipmentCacheAsync( equipment );
				return false;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "플레이어 장비 DB 저장 실패 : PlayerId={PlayerId}", equipment.PlayerId );
				return false;
			}
		}

		public async Task<bool> UpdateEquipmentRedisCacheAsync( EquipmentEntity equipment )
		{
			var cacheKey = $"{EQUIPMENT_CACHE_PREFIX}{equipment.PlayerId}";
			return await _redis.SetAsync( cacheKey, equipment, _cacheTTL );
		}

		// 캐시 무효화
		public async Task InvalidateEquipmentCacheAsync( EquipmentEntity equipment )
		{
			var playerId = equipment.PlayerId;
			string cacheKey = $"{EQUIPMENT_CACHE_PREFIX}{playerId}";
			try
			{
				await _redis.DeleteAsync( cacheKey );
				_logger.LogDebug( "장비 캐시 무효화: PlayerId={PlayerId}", playerId );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "장비 캐시 무효화 실패: PlayerId={PlayerId}", playerId );
			}
		}
	}
}
