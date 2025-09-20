using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Database.Entities;
using Server.Game;
using Server.Infra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Database.Services
{
	public class PlayerCacheService
	{
		private readonly IDbContextFactory<AppDbContext> _contextFactory;
		private readonly RedisService _redis;
		private readonly ILogger<PlayerCacheService> _logger;

		private const string PLAYER_CACHE_PREFIX = "MMO:player:";
		private readonly TimeSpan _cacheTTL = TimeSpan.FromHours(1);

		public PlayerCacheService(IDbContextFactory<AppDbContext> contextFactory, RedisService redis, ILogger<PlayerCacheService> logger)
		{
			_contextFactory = contextFactory;
			_redis=redis;
			_logger=logger;
		}

		// Cache-Aside 패턴 : 플레이어 조회
		public async Task<PlayerEntity> GetPlayerAsync(long playerId)
		{
			var cacheKey = $"{PLAYER_CACHE_PREFIX}{playerId}";

			try
			{
				// 1. Redis 먼저 확인
				var cachedPlayer = await _redis.GetAsync<PlayerEntity>(cacheKey);
				if (cachedPlayer != null)
				{
					_logger.LogDebug( "플레이어 캐시 히트 : {PlayerId}", playerId );
					return cachedPlayer;
				}

				// 2. DB에서 조회
				using var context = _contextFactory.CreateDbContext();
				var player = await context.Players.FirstOrDefaultAsync(p => p.PlayerId == playerId);
				if( player!= null )
				{
					// 3. Redis에 저장
					await _redis.SetAsync( cacheKey, player, _cacheTTL );
					_logger.LogDebug( "플레이어 DB -> Redis 캐시 : {PlayerId}", playerId );
				}

				return player;
			}
			catch (Exception ex)
			{
				_logger.LogError( ex, "플레이어 캐시 조회 실패 : {PlayerId}", playerId );
				using var context = _contextFactory.CreateDbContext();
				return await context.Players.FirstOrDefaultAsync( p => p.PlayerId == playerId );
			}
		}

		// Write-Through 패턴 : 플레이어 저장
		public async Task<bool> SavePlayerAsync(PlayerEntity player)
		{
			var cacheKey = $"{PLAYER_CACHE_PREFIX}{player.PlayerId}";

			try
			{
				// 1. DB 저장
				using var context = _contextFactory.CreateDbContext();
				player.UpdatedAt = DateTime.UtcNow;
				context.Players.Update( player );
				await context.SaveChangesAsync();

				// 2. Redis 업데이트
				await _redis.SetAsync( cacheKey, player, _cacheTTL );
				_logger.LogDebug( "플레이어 저장 및 캐시 업데이트 : {PlayerId}", player.PlayerId );

				return true;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "플레이어 저장 실패 : {PlayerId}", player.PlayerId );
				return false;
			}
		}

		// 캐시 무효화
		public async Task InvalidatePlayerCacheAsync(long playerId)
		{
			var cacheKey = $"{PLAYER_CACHE_PREFIX}{playerId}";

			try
			{
				await _redis.DeleteAsync( cacheKey );
				_logger.LogDebug( "플레이어 캐시 무효화 : {PlayerId}", playerId );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "플레이어 캐시 무효화 실패 : {PlayerId}", playerId );
			}
		}
	}
}
