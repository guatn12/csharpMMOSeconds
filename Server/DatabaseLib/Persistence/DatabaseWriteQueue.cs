using DatabaseLib.Options;
using DatabaseLib.Persistence.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DatabaseLib.Persistence
{
	public class DatabaseWriteQueue : IDatabaseWriteQueue
	{
		private const int Created = 0;
		private const int Running = 1;
		private const int Completing = 2;
		private const int Stopped = 3;
		private const int Faulted = 4;

		private readonly int _criticalEnqueueTimeoutMs;
		private readonly IDbContextFactory<AppDbContext> _contextFactory;
		private readonly ILogger<DatabaseWriteQueue> _logger;
		private readonly Channel<IDatabaseWriteRequest> _channel;
		private readonly IDatabaseWriteQueueObserver[] _observers;
		private readonly object _lifecycleLock = new object();

		private Task _workerTask;
		private int _state = Created;

		public DatabaseWriteQueue(IDbContextFactory<AppDbContext> contextFactory, IOptions<DatabaseOptions> options, IEnumerable<IDatabaseWriteQueueObserver> observers,
			ILogger<DatabaseWriteQueue> logger)
		{
			if(options == null)
				throw new ArgumentNullException( nameof( options ) );

			_contextFactory = contextFactory;
			_logger = logger;
			_observers = observers.ToArray();
			_criticalEnqueueTimeoutMs = options.Value.WriteQueueCriticalEnqueueTimeoutMs;

			var channelOptions = new BoundedChannelOptions(options.Value.WriteQueueCapacity)
			{
				SingleReader = true,
				SingleWriter = false,
				FullMode = BoundedChannelFullMode.Wait,
			};

			_channel = Channel.CreateBounded<IDatabaseWriteRequest>( channelOptions );
		}

		public Task StartAsync()
		{
			lock(_lifecycleLock)
			{
				if(_state == Running)
					return Task.CompletedTask;

				if(_state != Created)
					throw new InvalidOperationException( "DatabaseWriteQueue cannot be restarted." );

				_state = Running;
				_workerTask = Task.Run( WorkerLoopAsync );
			}

			_logger.LogInformation( "DatabaseWriteQueue started." );
			return Task.CompletedTask;
		}

		public Task<DatabaseWriteResult<TResult>> EnqueueCriticalAsync<TResult>(IDatabaseWriteCommand<TResult> command, CancellationToken cancellationToken = default)
		{
			return EnqueueCriticalAsync( command, _criticalEnqueueTimeoutMs, cancellationToken );
		}

		public async Task<DatabaseWriteResult<TResult>> EnqueueCriticalAsync<TResult>(IDatabaseWriteCommand<TResult> command, int enqueueTimeoutMs, CancellationToken cancellationToken = default)
		{
			if(command == null)
				throw new ArgumentNullException( nameof( command ) );

			if(Volatile.Read(ref _state) != Running)
			{
				NotifyObservers( observer => observer.RecordResult( DatabaseWriteStatus.QueueStopping ) );
				return DatabaseWriteResult<TResult>.WithoutValue( DatabaseWriteStatus.QueueStopping, "QueueStopping" );
			}

			var request = new DatabaseWriteRequest<TResult>(command);
			// 타임 아웃 설정 - Default값보다 클 경우 Default로 처리.
			int effectiveTimeoutMs = Math.Min(enqueueTimeoutMs, _criticalEnqueueTimeoutMs);
			using var timeoutSource = new CancellationTokenSource(effectiveTimeoutMs);
			using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

			try
			{
				await _channel.Writer.WriteAsync( request, linkedSource.Token );
				NotifyObservers( observer => observer.SetQueueDepth( GetQueueDepth() ) );
			}
			catch(OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				NotifyObservers( observer => observer.RecordResult( DatabaseWriteStatus.Cancelled ) );
				return DatabaseWriteResult<TResult>.WithoutValue( DatabaseWriteStatus.Cancelled, "EnqueueCancelled" );
			}
			catch(OperationCanceledException) when (timeoutSource.IsCancellationRequested)
			{
				NotifyObservers( observer => observer.RecordResult( DatabaseWriteStatus.EnqueueTimeout ) );
				return DatabaseWriteResult<TResult>.WithoutValue( DatabaseWriteStatus.EnqueueTimeout, "CriticalEnqueueTimeout" );
			}
			catch(ChannelClosedException)
			{
				NotifyObservers( observer => observer.RecordResult( DatabaseWriteStatus.QueueStopping ) );
				return DatabaseWriteResult<TResult>.WithoutValue( DatabaseWriteStatus.QueueStopping, "QueueStopping" );
			}

			// 수락 뒤에는 호출자 token으로 대기를 취소하지 않는다.
			return await request.Completion;
		}

		public DatabaseWriteEnqueueStatus TryEnqueue<TResult>(IDatabaseWriteCommand<TResult> command, out Task<DatabaseWriteResult<TResult>> completion)
		{
			if(command == null)
				throw new ArgumentNullException( nameof( command ) );

			completion = null;

			if(Volatile.Read( ref _state ) != Running)
				return DatabaseWriteEnqueueStatus.QueueStopping;

			var request = new DatabaseWriteRequest<TResult>(command);
			if(_channel.Writer.TryWrite(request) == false)
			{
				if(Volatile.Read(ref _state) == Running)
				{
					NotifyObservers( observer => observer.RecordQueueFull() );
					return DatabaseWriteEnqueueStatus.QueueFull;
				}

				return DatabaseWriteEnqueueStatus.QueueStopping;
			}

			NotifyObservers( observer => observer.SetQueueDepth( GetQueueDepth() ) );
			completion = request.Completion;
			return DatabaseWriteEnqueueStatus.Accepted;
		}

		public async Task CompleteAndDrainAsync()
		{
			Task workerTask;

			lock(_lifecycleLock)
			{
				if(_state == Created)
				{
					_state = Stopped;
					_channel.Writer.TryComplete();
					return;
				}

				if(_state == Running)
				{
					_state = Completing;
					_channel.Writer.TryComplete();
				}

				workerTask = _workerTask;
			}

			if(workerTask != null)
				await workerTask;

			if(Volatile.Read( ref _state ) != Faulted)
				Volatile.Write( ref _state, Stopped );

			_logger.LogInformation( "DatabaseWriteQueue drained and stopped." );
		}

		private async Task WorkerLoopAsync()
		{
			IDatabaseWriteRequest current = null;

			try
			{
				await foreach(var request in _channel.Reader.ReadAllAsync())
				{
					NotifyObservers( observer => observer.SetQueueDepth( GetQueueDepth() ) );
					current = request;

					await request.ExecuteAsync( this );
					current = null;
				}
			}
			catch(Exception ex)
			{
				Volatile.Write( ref _state, Faulted );
				_channel.Writer.TryComplete( ex );
				current?.Fail( ex );

				while(_channel.Reader.TryRead( out var pending ))
					pending.Fail( ex );

				NotifyObservers(observer => observer.SetQueueDepth( GetQueueDepth() ) );
				_logger.LogCritical( ex, "DatabaseWriteQueue worker sopped unexpectedly." );
				throw;
			}
		}

		private async Task<DatabaseWriteResult<TResult>> ExecuteCommandAsync<TResult>(IDatabaseWriteCommand<TResult> command)
		{
			bool commitStarted = false;

			try
			{
				await using var context = await _contextFactory.CreateDbContextAsync();
				await using var transaction = await context.Database.BeginTransactionAsync();

				DatabaseWriteDecision decision = await command.ApplyAsync(context, CancellationToken.None);

				if(decision.ShouldCommit == false)
				{
					await transaction.RollbackAsync();
					return DatabaseWriteResult<TResult>.WithoutValue( DatabaseWriteStatus.Rejected,
						decision.ErrorCode );
				}

				await context.SaveChangesAsync();
				TResult value = command.BuildResultAfterSave();

				commitStarted = true;
				await transaction.CommitAsync();

				return DatabaseWriteResult<TResult>.Committed( value );
			}
			catch(DbUpdateConcurrencyException ex)
			{
				_logger.LogWarning( ex, "DB concurrency conflict. Command={Command}", command.Name );
				return DatabaseWriteResult<TResult>.WithoutValue(
					DatabaseWriteStatus.ConcurrencyConflict,
					"ConcurrencyConflict" );
			}
			catch(Exception ex) when(IsCritical(ex) == false)
			{
				DatabaseWriteStatus status = commitStarted
					? DatabaseWriteStatus.OutcomeUnknown
					: DatabaseWriteStatus.Failed;

				_logger.LogError( ex, "DB command failed. Command={Command}, Status={Status}", command.Name, status );

				return DatabaseWriteResult<TResult>.WithoutValue( status,
					status == DatabaseWriteStatus.OutcomeUnknown
					? "CommitOutcomeUnknown"
					: "DatabaseWriteFailed" );
			}
		}

		private static bool IsCritical( Exception ex )
		{
			return ex is OutOfMemoryException
				|| ex is AccessViolationException
				|| ex is AppDomainUnloadedException;
		}

		private int GetQueueDepth()
		{
			return _channel.Reader.CanCount ? _channel.Reader.Count : 0;
		}

		private interface IDatabaseWriteRequest
		{
			ValueTask ExecuteAsync( DatabaseWriteQueue owner );
			void Fail( Exception ex );
		}

		private sealed class DatabaseWriteRequest<TResult> : IDatabaseWriteRequest
		{
			private readonly IDatabaseWriteCommand<TResult> _command;
			private readonly TaskCompletionSource<DatabaseWriteResult<TResult>> _completion;

			public Task<DatabaseWriteResult<TResult>> Completion => _completion.Task;

			public DatabaseWriteRequest( IDatabaseWriteCommand<TResult> command)
			{
				_command = command;
				_completion = new TaskCompletionSource<DatabaseWriteResult<TResult>>( TaskCreationOptions.RunContinuationsAsynchronously );
			}

			public async ValueTask ExecuteAsync(DatabaseWriteQueue owner)
			{
				long startedAt = Stopwatch.GetTimestamp();

				DatabaseWriteResult<TResult> result = await owner.ExecuteCommandAsync(_command);
				_completion.TrySetResult( result );

				double elapsedSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;

				owner.NotifyObservers( observer => observer.RecordResult( result.Status ) );
				owner.NotifyObservers( observer => observer.ObserveProcessingDuration( elapsedSeconds ) );
			}

			public void Fail( Exception ex )
			{
				_completion.TrySetException( ex );
			}
		}

		private void NotifyObservers( Action<IDatabaseWriteQueueObserver> notify )
		{
			foreach(IDatabaseWriteQueueObserver observer in _observers)
			{
				try
				{
					notify( observer );

				}
				catch(Exception ex)
				{
					_logger.LogWarning( ex, "DatabaseWriteQueue observer threw an exception." );
				}
			}
		}
	}
}
