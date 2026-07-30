using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DatabaseLib.Entities;
using DatabaseLib.Redis;

namespace DatabaseLib.Services
{
	public class InventoryCacheService
	{
		private readonly IDbContextFactory<AppDbContext> _contextFactory;
		private readonly IRedisService _redis;
		private readonly ILogger<InventoryCacheService> _logger;

		private const string INVENTORY_CACHE_PREFIX = "inventory:";
		private readonly TimeSpan _cacheTTL = TimeSpan.FromMinutes(30);

		public InventoryCacheService(IDbContextFactory<AppDbContext> contextFactory, IRedisService redis, ILogger<InventoryCacheService> logger )
		{
			_contextFactory = contextFactory;
			_redis=redis;
			_logger=logger;
		}

		// 인벤토리 조회
		public async Task<InventoryEntity> GetPlayerInventoryAsync(long playerId)
		{
			string cacheKey = $"{INVENTORY_CACHE_PREFIX}{playerId}";

			try
			{
				InventoryEntity cachedInventory = await _redis.GetAsync<InventoryEntity>(cacheKey);
				if (cachedInventory != null)
				{
					_logger.LogDebug( "인벤토리 캐시 히트: PlayerId={PlayerId}", playerId );
					return cachedInventory;
				}

				using var context = _contextFactory.CreateDbContext();
				InventoryEntity inventory = await context.Inventory.FirstOrDefaultAsync(i => i.PlayerId == playerId);
				if (inventory != null)
				{
					await _redis.SetAsync( cacheKey, inventory, _cacheTTL );
					_logger.LogDebug( "인벤토리 DB → Redis: PlayerId={PlayerId}", playerId );
				}

				return inventory;
			}
			catch (Exception ex)
			{
				using var context = _contextFactory.CreateDbContext();
				_logger.LogError(ex, "인벤토리 조회 실패: PlayerId={PlayerId}", playerId);
				return await context.Inventory.FirstOrDefaultAsync(i =>i.PlayerId == playerId);
			}
		}

		public async Task<bool> SaveInventoryToDbAsync( InventoryEntity inventory )
		{
			try
			{
				using var context = _contextFactory.CreateDbContext();
				context.Inventory.Update( inventory );

				inventory.LastUpdated = DateTime.UtcNow;
				inventory.Version++;    // 낙관적 동시성 제어

				await context.SaveChangesAsync();

				return true;
			}
			catch(DbUpdateConcurrencyException ex)
			{
				_logger.LogWarning( ex, "인벤토리 동시성 충돌: PlayerId={PlayerId}", inventory.PlayerId );
				await InvalidateInventoryCacheAsync( inventory );
				return false;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "플레이어 인벤토리 DB 저장 실패 : PlayerId={PlayerId}", inventory.PlayerId );
				return false;
			}
		}

		public async Task<bool> UpdateInventoryRedisCacheAsync( InventoryEntity inventory )
		{
			var cacheKey = $"{INVENTORY_CACHE_PREFIX}{inventory.PlayerId}";
			return await _redis.SetAsync( cacheKey, inventory, _cacheTTL );
		}

		// 캐시 무효화
		public async Task InvalidateInventoryCacheAsync(InventoryEntity inventory)
		{
			var playerId = inventory.PlayerId;
			string cacheKey = $"{INVENTORY_CACHE_PREFIX}{playerId}";
			try
			{
				await _redis.DeleteAsync( cacheKey );
				_logger.LogDebug( "인벤토리 캐시 무효화: PlayerId={PlayerId}", playerId );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "인벤토리 캐시 무효화 실패: PlayerId={PlayerId}", playerId );
			}
		}
	}
}
