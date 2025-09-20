using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Database.Entities;
using Server.Infra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Database.Services
{
	public class InventoryCacheService
	{
		private readonly IDbContextFactory<AppDbContext> _contextFactory;
		private readonly RedisService _redis;
		private readonly ILogger<InventoryCacheService> _logger;

		private const string INVENTORY_CACHE_PREFIX = "MMO:inventory:";
		private readonly TimeSpan _cacheTTL = TimeSpan.FromMinutes(30);

		public InventoryCacheService(IDbContextFactory<AppDbContext> contextFactory, RedisService redis, ILogger<InventoryCacheService> logger )
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

		// 인벤토리 저장 (Write-Through + 낙관적 동시성 제어)
		public async Task<bool> SaveInventoryAsync(InventoryEntity inventory)
		{
			string cacheKey = $"{INVENTORY_CACHE_PREFIX}{inventory.PlayerId}";

			try
			{
				inventory.LastUpdated = DateTime.UtcNow;
				inventory.Version++;    // 낙관적 동시성 제어

				using var context = _contextFactory.CreateDbContext();
				context.Inventory.Update( inventory );
				await context.SaveChangesAsync();

				await _redis.SetAsync( cacheKey, inventory, _cacheTTL );
				_logger.LogDebug( "인벤토리 저장: PlayerId={PlayerId}, Version={Version}",
					inventory.PlayerId, inventory.Version );

				return true;
			}
			catch(DbUpdateConcurrencyException ex)
			{
				_logger.LogWarning( ex, "인벤토리 동시성 충돌: PlayerId={PlayerId}", inventory.PlayerId );
				await InvalidateInventoryCacheAsync( inventory.PlayerId );
				return false;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "인벤토리 저장 실패: PlayerId={PlayerId}", inventory.PlayerId );
				return false;
			}
		}

		// 아이템 추가 편의 메서드
		public async Task<bool> AddItemAsync(long playerId, int itemId, int quantity)
		{
			try
			{
				InventoryEntity inventory = await GetPlayerInventoryAsync(playerId);
				if(inventory == null)
					return false;

				InventoryModel data = inventory.InventoryData;

				// 빈 슬롯 찾기
				var usedSlots = data.Items.Select(i => i.Slot).ToHashSet();
				var emptySlot = Enumerable.Range(0, inventory.MaxSlots)
					.FirstOrDefault(slot => !usedSlots.Contains(slot));

				InventoryItem newItem = new InventoryItem
				{
					ItemId = itemId,
					Quantity = quantity,
					Slot = emptySlot,
					AcquiredAt = DateTime.UtcNow
				};

				data.Items.Add( newItem );
				inventory.InventoryData = data;

				return await SaveInventoryAsync( inventory );
			}
			catch (Exception ex)
			{
				_logger.LogError( ex, "아이템 추가 실패: PlayerId={PlayerId}, ItemId={ItemId}", playerId, itemId );
				return false;
			}
		}

		// 캐시 무효화
		public async Task InvalidateInventoryCacheAsync(long playerId)
		{
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
