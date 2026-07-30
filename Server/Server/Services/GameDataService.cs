using DatabaseLib;
using DatabaseLib.Entities;
using DatabaseLib.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services
{
	public class GameDataService : IGameDataService
	{
		private readonly PlayerCacheService _playerCache;
		private readonly InventoryCacheService _inventoryCache;
		private readonly EquipmentCacheService _equipmentCache;
		private readonly IDbContextFactory<AppDbContext> _contextFactory;
		private readonly ILogger<GameDataService> _logger;
		private readonly ConcurrentDictionary<long, Task> _pendingFlush = new();
		
		const int MaxRetryCount = 3;

		public GameDataService( PlayerCacheService playerCache, InventoryCacheService inventoryCache, 
			EquipmentCacheService equipmentCache, IDbContextFactory<AppDbContext> contextFactory, ILogger<GameDataService> logger )
		{
			_playerCache = playerCache;
			_inventoryCache = inventoryCache;
			_equipmentCache = equipmentCache;
			_contextFactory = contextFactory;
			_logger = logger;
		}

		public async Task<PlayerAggregate> LoadPlayerAggregateAsync( long accountId, long playerId )
		{
			if(_pendingFlush.TryGetValue(playerId, out var pending))
			{
				try
				{
					await pending.WaitAsync( TimeSpan.FromSeconds( 5 ) );
				}
				catch(TimeoutException)
				{
					_logger.LogWarning( "종료 flush 대기 타임아웃: PlayerId={PlayerId}", playerId );
				}
			}

			// 플레이어 데이터 로드
			PlayerEntity playerEntity = await _playerCache.GetPlayerAsync( accountId, playerId );
			if(playerEntity == null)
			{
				_logger.LogWarning( "플레이어 데이터 없음: PlayerId={PlayerId}", playerId );
				return null;
			}

			// 인벤토리 데이터 로드
			InventoryEntity inventoryEntity = await _inventoryCache.GetPlayerInventoryAsync( playerId );

			// 장비 데이터 로드
			EquipmentEntity equipmentEntity = await _equipmentCache.GetPlayerEquipmentAsync( playerId );

			return new PlayerAggregate
			{
				Player = playerEntity,
				Inventory = inventoryEntity,
				Equipment = equipmentEntity
			};
		}

		public async Task<bool> SavePlayerAggregateAsync( PlayerAggregate aggregate )
		{
			bool playerSaved = await SaveWithRetryAsync(_playerCache.SavePlayerToDbAsync, _playerCache.UpdatePlayerRedisCacheAsync, 
				_playerCache.InvalidatePlayerCacheAsync, aggregate.Player);
			bool inventorySaved = true;
			bool equipmentSaved = true;

			if(aggregate.Inventory != null)
			{
				inventorySaved = await SaveWithRetryAsync( _inventoryCache.SaveInventoryToDbAsync, _inventoryCache.UpdateInventoryRedisCacheAsync,
					_inventoryCache.InvalidateInventoryCacheAsync, aggregate.Inventory );
			}

			if(aggregate.Equipment != null)
			{
				equipmentSaved = await SaveWithRetryAsync( _equipmentCache.SaveEquipmentToDbAsync, _equipmentCache.UpdateEquipmentRedisCacheAsync,
					_equipmentCache.InvalidateEquipmentCacheAsync, aggregate.Equipment );
			}

			if(!playerSaved || !inventorySaved || !equipmentSaved)
			{
				_logger.LogError( "플레이어 데이터 저장 실패: PlayerId={PlayerId}, Player={PlayerOK}, Inventory={InvenOK}, Equipment={EquipOK}",
					aggregate.Player.PlayerId, playerSaved, inventorySaved, equipmentSaved );
			}

			return playerSaved && inventorySaved && equipmentSaved;
		}

		public async Task<List<PlayerEntity>> GetPlayerListByAccountIdAsync( long accountId )
		{
			return await _playerCache.GetPlayerListByAccountIdAsync( accountId);
		}

		public async Task<bool> IsPlayerNameTakenAsync( string playerName )
		{
			return await _playerCache.IsPlayerNameAsync( playerName );
		}

		public async Task<bool> IsPlayerOwnedByAccountAsync( long accountId, long playerId )
		{
			return await _playerCache.IsPlayerOwnedByAccountAsync( accountId, playerId );
		}

		public async Task<PlayerAggregate> CreateNewPlayerAsync( string playerName, long accountId )
		{
			try
			{
				using var context = _contextFactory.CreateDbContext();
				await using var transaction = await context.Database.BeginTransactionAsync();

				var playerEntity = new PlayerEntity
				{
					AccountId = accountId,
					PlayerName = playerName,
					Level = 1,
					Experience = 0,
					CreatedAt = DateTime.UtcNow,
					UpdatedAt = DateTime.UtcNow
				};
				context.Players.Add( playerEntity );

				await context.SaveChangesAsync();
				long playerId = playerEntity.PlayerId;

				var inventoryEntity = new InventoryEntity
				{
					PlayerId = playerId,
					MaxSlots = 50,
					CreatedAt = DateTime.UtcNow,
					LastUpdated = DateTime.UtcNow,
				};

				var equipmentEntity = new EquipmentEntity
				{
					PlayerId = playerId,
					CreatedAt = DateTime.UtcNow,
					LastUpdated = DateTime.UtcNow
				};

				context.Inventory.Add( inventoryEntity );
				context.Equipment.Add( equipmentEntity );
				await context.SaveChangesAsync();
				await transaction.CommitAsync();

				await _playerCache.InvalidatePlayerCacheAsync( playerEntity );

				_logger.LogInformation( "새 플레이어 생성: PlayerId={PlayerId}, name={PlayerName}", playerId, playerName );

				return new PlayerAggregate
				{
					Player = playerEntity,
					Inventory = inventoryEntity,
					Equipment = equipmentEntity,
				};
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "새 플레이어 생성 실패 " );
				return null;
			}
		}

		private async Task<bool> SaveWithRetryAsync<T>(Func<T, Task<bool>> dbSave, Func<T, Task> redisUpdate, Func<T, Task> redisRemove, 
			T entity)
		{
			var entityLabel = typeof(T).Name;
			// 1. DB 저장 시도
			bool dbOk = await dbSave(entity);
			if(!dbOk)
			{
				_logger.LogError( "DB 저장 실패: {EntityLabel} = {Entity}", entityLabel, entity );
				return false;
			}

			// 2. Redis 업데이트  + 50/100/200ms 지수 백오프 재시도
			Exception lastEx = null;
			int[] backOffMs = {50, 100, 200 };
			for(int attempt = 0; attempt < MaxRetryCount; attempt++)
			{
				try
				{
					await redisUpdate( entity );
					return true;		// 정상 케이스 - redis도 최신
				}
				catch(Exception ex)
				{
					lastEx = ex;
					_logger.LogWarning( ex, "Redis 업데이트 실패 (attempt {Attempt}/{MaxRetryCount}), retry in {Delay}ms: {EntityLabel}",
						attempt+1, MaxRetryCount, backOffMs[ attempt ], entityLabel );
					if(attempt < MaxRetryCount - 1) 
						await Task.Delay( backOffMs[ attempt ] );
				}
			}

			// 3. Redis 업데이트 실패 시, redis 키 삭제
			_logger.LogError( lastEx, "Redis 갱신 실패, 키 삭제로 fallback: {EntityLabel}", entityLabel );
			try
			{
				await redisRemove( entity );
			}
			catch(Exception ex)
			{
				_logger.LogError(ex, "Redis 키 삭제 실패. {EntityLabel}", entityLabel );
			}

			return true;

		}
	}
}
