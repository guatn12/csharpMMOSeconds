
using DatabaseLib.Persistence;
using DatabaseLib.Redis;
using DatabaseLib.Services;
using Microsoft.Extensions.Logging;
using Server.Infra.Metrics;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Services.Persistence
{
	public sealed class PlayerPersistenceService : IPlayerPersistenceService
	{
		private static readonly int[] CacheRetryDelayMs = {50, 100, 200 };

		private readonly IDatabaseWriteQueue _writeQueue;
		private readonly IRedisService _redisService;
		private readonly ILogger<PlayerPersistenceService> _logger;

		public PlayerPersistenceService( IDatabaseWriteQueue writeQueue, IRedisService redisService, ILogger<PlayerPersistenceService> logger )
		{
			_writeQueue = writeQueue;
			_redisService = redisService;
			_logger = logger;
		}

		public async Task<DatabaseWriteResult<PlayerSaveCommit>> SaveAsync( PlayerSaveSnapshot snapshot, CancellationToken cancellationToken = default )
		{
			DatabaseWriteResult<PlayerSaveCommit> result = await _writeQueue.EnqueueCriticalAsync(new SavePlayerCommand(snapshot), cancellationToken);

			await InvalidateAfterSaveAsync( snapshot.AccountId, snapshot.PlayerRawId, result.Status );
			return result;
		}

		public DatabaseWriteEnqueueStatus TryEnqueueCheckpoint(PlayerSaveSnapshot snapshot,
			out Task<DatabaseWriteResult<PlayerSaveCommit>> completion)
		{
			DatabaseWriteEnqueueStatus status = _writeQueue.TryEnqueue(new SavePlayerCommand(snapshot),
				out Task<DatabaseWriteResult<PlayerSaveCommit>> dbCompletion);

			if(status != DatabaseWriteEnqueueStatus.Accepted)
			{
				completion = null;
				return status;
			}

			completion = FinishCheckpointAsync( snapshot, dbCompletion );
			return DatabaseWriteEnqueueStatus.Accepted;
		}

		public async Task<DatabaseWriteResult<CreatePlayerCommit>> CreatePlayerAsync(long accountId, string playerName, CancellationToken cancellationToken = default)
		{
			DatabaseWriteResult<CreatePlayerCommit> result = await _writeQueue.EnqueueCriticalAsync(new CreatePlayerCommand(accountId, playerName), cancellationToken);

			if(result.Status == DatabaseWriteStatus.Committed || result.Status == DatabaseWriteStatus.OutcomeUnknown)
			{
				await InvalidateWithRetryAsync( accountId, result.HasValue ? result.Value.PlayerRawId : 0 );
			}

			return result;
		}

		private async Task<DatabaseWriteResult<PlayerSaveCommit>> FinishCheckpointAsync(PlayerSaveSnapshot snapshot, Task<DatabaseWriteResult<PlayerSaveCommit>> dbCompletion)
		{
			DatabaseWriteResult<PlayerSaveCommit> result = await dbCompletion;
			await InvalidateAfterSaveAsync( snapshot.AccountId, snapshot.PlayerRawId, result.Status );
			return result;
		}

		private async Task InvalidateAfterSaveAsync(long accountId, long playerRawId, DatabaseWriteStatus status)
		{
			if( status != DatabaseWriteStatus.Committed && status != DatabaseWriteStatus.ConcurrencyConflict &&
				status != DatabaseWriteStatus.OutcomeUnknown)
			{
				return;
			}

			await InvalidateWithRetryAsync( accountId, playerRawId );
		}

		private async Task InvalidateWithRetryAsync(long accountId, long playerRawId)
		{
			Exception lastException = null;

			for(int attempt = 0; attempt < CacheRetryDelayMs.Length; attempt++)
			{
				try
				{
					IRedisBatch batch = _redisService.CreateBatch();
					batch.KeyDelete( PlayerPersistenceCacheKeys.Account( accountId ) );
					if(0 < playerRawId)
					{
						batch.KeyDelete( PlayerPersistenceCacheKeys.Inventory( playerRawId ) );
						batch.KeyDelete( PlayerPersistenceCacheKeys.Equipment( playerRawId ) );
						batch.KeyDelete( PlayerPersistenceCacheKeys.State( playerRawId ) );
					}

					await batch.ExecuteAsync();
					return;
				}
				catch(Exception ex)
				{
					lastException = ex;
					if(attempt + 1 < CacheRetryDelayMs.Length)
						await Task.Delay( CacheRetryDelayMs[ attempt ] );
				}
			}

			try
			{
				GameMetrics.CacheInvalidationFailures.Inc();
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "Cache invalidation failure metric recording failed." );
			}
			_logger.LogError( lastException, "DB Commit 뒤 Player 캐시 무효화 실패: AccountId={AccountId}, PlayerId={PlayerId}", accountId, playerRawId );
		}
	}
}
