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
			string cacheKey = PlayerPersistenceCacheKeys.Inventory(playerId);

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
	}
}
