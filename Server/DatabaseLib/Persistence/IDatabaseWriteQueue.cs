using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseLib.Persistence
{
	public interface IDatabaseWriteQueue
	{
		Task StartAsync();

		DatabaseWriteEnqueueStatus TryEnqueue<TResult>( IDatabaseWriteCommand<TResult> command, out Task<DatabaseWriteResult<TResult>> completion );

		Task<DatabaseWriteResult<TResult>> EnqueueCriticalAsync<TResult>( IDatabaseWriteCommand<TResult> command, CancellationToken cancellationToken = default );
		Task<DatabaseWriteResult<TResult>> EnqueueCriticalAsync<TResult>( IDatabaseWriteCommand<TResult> command, int enqueueTimeoutMs, CancellationToken cancellationToken = default );

		Task CompleteAndDrainAsync();
	}
}
