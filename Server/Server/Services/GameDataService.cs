using DatabaseLib;
using DatabaseLib.Entities;
using DatabaseLib.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Game;
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
		private readonly PlayerStateCacheService _playerStateCache;
		private readonly InventoryCacheService _inventoryCache;
		private readonly EquipmentCacheService _equipmentCache;
		private readonly ILogger<GameDataService> _logger;

		public GameDataService( PlayerCacheService playerCache, PlayerStateCacheService playerStateCache, InventoryCacheService inventoryCache, 
			EquipmentCacheService equipmentCache, ILogger<GameDataService> logger )
		{
			_playerCache = playerCache;
			_playerStateCache = playerStateCache;
			_inventoryCache = inventoryCache;
			_equipmentCache = equipmentCache;
			_logger = logger;
		}

		public async Task<PlayerAggregate> LoadPlayerAggregateAsync( long accountId, long playerId )
		{
			//if(_pendingFlush.TryGetValue(playerId, out var pending))
			//{
			//	try
			//	{
			//		await pending.WaitAsync( TimeSpan.FromSeconds( 5 ) );
			//	}
			//	catch(TimeoutException)
			//	{
			//		_logger.LogWarning( "종료 flush 대기 타임아웃: PlayerId={PlayerId}", playerId );
			//	}
			//}

			// 플레이어 데이터 로드
			PlayerEntity playerEntity = await _playerCache.GetPlayerAsync( accountId, playerId );
			if(playerEntity == null)
			{
				_logger.LogWarning( "플레이어 데이터 없음: PlayerId={PlayerId}", playerId );
				return null;
			}

			// 플레이어 상태 데이터 로드
			PlayerStateEntity playerStateEntity = await _playerStateCache.GetPlayerStateAsync(playerId);
			if(playerStateEntity == null)
			{
				_logger.LogError( "PlayerState 데이터 없음: PlayerId={PlayerId}", playerId );
				return null;
			}

			// 인벤토리 데이터 로드
			InventoryEntity inventoryEntity = await _inventoryCache.GetPlayerInventoryAsync( playerId );

			// 장비 데이터 로드
			EquipmentEntity equipmentEntity = await _equipmentCache.GetPlayerEquipmentAsync( playerId );

			return new PlayerAggregate
			{
				Player = playerEntity,
				PlayerState = playerStateEntity,
				Inventory = inventoryEntity,
				Equipment = equipmentEntity
			};
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
	}
}
