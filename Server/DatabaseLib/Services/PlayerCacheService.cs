using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DatabaseLib.Entities;
using DatabaseLib.Redis;
using System.Text.Json;

namespace DatabaseLib.Services
{
	public class PlayerCacheService
	{
		private readonly IDbContextFactory<AppDbContext> _contextFactory;
		private readonly IRedisService _redis;
		private readonly ILogger<PlayerCacheService> _logger;

		private const string PLAYER_ACCOUNT_CACHE_PREFIX = "account:";
		private readonly TimeSpan _cacheTTL = TimeSpan.FromHours(1);

		public PlayerCacheService(IDbContextFactory<AppDbContext> contextFactory, IRedisService redis, ILogger<PlayerCacheService> logger)
		{
			_contextFactory = contextFactory;
			_redis=redis;
			_logger=logger;
		}

		// Cache-Aside 패턴 : 플레이어 조회
		public async Task<PlayerEntity> GetPlayerAsync( long accountId, long playerId )
		{
			var cacheKey = $"{PLAYER_ACCOUNT_CACHE_PREFIX}{accountId}";

			try
			{
				// 1. Redis 먼저 확인
				var cachedPlayer = await _redis.HashGetAsync<PlayerEntity>( cacheKey, playerId.ToString() );
				if (cachedPlayer != null)
				{
					_logger.LogDebug( "플레이어 캐시 히트 : {PlayerId}", playerId );
					return cachedPlayer;
				}

				// 2. DB에서 조회
				using var context = _contextFactory.CreateDbContext();
				var players = await context.Players.AsNoTracking()
					.Where(p => p.AccountId == accountId)
					.ToListAsync();
				if( players != null )
				{
					// 3. Redis에 저장 - 키 삭제(불완전 데이터가 있는 경우) 후 모든 캐릭터 데이터 재업로드
					Dictionary<string, string> playerDict = new Dictionary<string, string>();
					var batch = _redis.CreateBatch();

					batch.KeyDelete( cacheKey );

					foreach(var player in players)
					{
						playerDict.TryAdd( player.PlayerId.ToString(), JsonSerializer.Serialize( player ) );
					}

					batch.HashSet( cacheKey, playerDict );
					batch.KeyExpire( cacheKey, _cacheTTL );

					// redis 저장
					await batch.ExecuteAsync();
					_logger.LogDebug( "플레이어 DB -> Redis 캐시 : AccountID={AccountId}/PlayerId={PlayerId}", accountId, playerId );
				}

				var returnPlayer = players.Where( p => p.PlayerId == playerId ).SingleOrDefault();

				return returnPlayer;
			}
			catch (Exception ex)
			{
				_logger.LogError( ex, "플레이어 캐시 조회 실패 : {PlayerId}", playerId );
				using var context = _contextFactory.CreateDbContext();
				return await context.Players.FirstOrDefaultAsync( p => p.PlayerId == playerId );
			}
		}

		public async Task<List<PlayerEntity>> GetPlayerListByAccountIdAsync(long accountId)
		{
			var cacheKey = $"{PLAYER_ACCOUNT_CACHE_PREFIX}{accountId}";
			
			try
			{
				// 1. redis 먼저 확인
				var cachedPlayers = await _redis.HashGetAllAsync(cacheKey);
				if( cachedPlayers != null && cachedPlayers.Count != 0)
				{
					List<PlayerEntity> returnValue = new List<PlayerEntity>();

					_logger.LogDebug( "플레이어 리스트 캐시 히트 : {AccountId}", accountId );
					foreach(var cachePlayer in cachedPlayers.Values)
					{
						var playerEntity = JsonSerializer.Deserialize<PlayerEntity>(cachePlayer);
						returnValue.Add( playerEntity );
					}
					return returnValue;
				}

				// db조회
				using var context = _contextFactory.CreateDbContext();
				var players = await context.Players.AsNoTracking()
					.Where(p => p.AccountId == accountId)
					.ToListAsync();
				if(players != null)
				{
					Dictionary<string, string> playerDict = new Dictionary<string, string>();
					var batch = _redis.CreateBatch();
					batch.KeyDelete( cacheKey );

					foreach(var player in players)
					{
						playerDict.TryAdd( player.PlayerId.ToString(), JsonSerializer.Serialize( player ) );
					}

					batch.HashSet( cacheKey, playerDict );
					batch.KeyExpire( cacheKey, _cacheTTL );

					// redis 저장
					await batch.ExecuteAsync();
					_logger.LogDebug( "플레이어 DB -> Redis 캐시 : AccountID={AccountId}", accountId );
				}

				return players;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "플레이어 리스트 캐시 조회 실패 : {AccountId}", accountId );
				using var context = _contextFactory.CreateDbContext();
				return await context.Players.AsNoTracking()
					.Where( p => p.AccountId == accountId )
					.ToListAsync();
			}
		}

		public async Task<bool> IsPlayerNameAsync(string playerName)
		{
			using var context = _contextFactory.CreateDbContext();
			return await context.Players.AsNoTracking()
				.AnyAsync( p => p.PlayerName == playerName );
		}

		public async Task<bool> IsPlayerOwnedByAccountAsync(long accountId, long playerId)
		{
			using var context = _contextFactory.CreateDbContext();
			return await context.Players.AsNoTracking()
				.AnyAsync( p => p.PlayerId == playerId && p.AccountId == accountId );
		}

		public async Task<bool> SavePlayerToDbAsync(PlayerEntity player)
		{
			try
			{
				using var context = _contextFactory.CreateDbContext();
				player.UpdatedAt = DateTime.UtcNow;
				context.Players.Update( player );
				await context.SaveChangesAsync();

				return true;
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "플레이어 DB 저장 실패 : PlayerId={PlayerId}", player.PlayerId );
				return false;
			}
		}

		public async Task<bool> UpdatePlayerRedisCacheAsync(PlayerEntity player)
		{
			var cacheKey = $"{PLAYER_ACCOUNT_CACHE_PREFIX}{player.AccountId}";
			bool keyExists = await _redis.KeyExistsAsync( cacheKey );
			if(!keyExists)
				return true;

			await _redis.HashSetAsync( cacheKey, new Dictionary<string, string> { { player.PlayerId.ToString(), 
					JsonSerializer.Serialize(player)} }, _cacheTTL );

			return true;
		}

		// 캐시 무효화
		public async Task InvalidatePlayerCacheAsync(PlayerEntity player)
		{
			var cacheKey = $"{PLAYER_ACCOUNT_CACHE_PREFIX}{player.AccountId}";

			try
			{
				await _redis.DeleteAsync( cacheKey );
				_logger.LogDebug( "플레이어 캐시 무효화 : {AccountId}", player.AccountId );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "플레이어 캐시 무효화 실패 : {AccountId}", player.AccountId );
			}
		}
	}
}
