using DatabaseLib.Persistence;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Services.Persistence
{
	public interface IPlayerPersistenceService
	{
		Task<DatabaseWriteResult<PlayerSaveCommit>> SaveAsync( PlayerSaveSnapshot snapshot, CancellationToken cancellationToken = default );
		DatabaseWriteEnqueueStatus TryEnqueueCheckpoint( PlayerSaveSnapshot snapshot, out Task<DatabaseWriteResult<PlayerSaveCommit>> completion );
		Task<DatabaseWriteResult<CreatePlayerCommit>> CreatePlayerAsync( long accountId, string playerName, CancellationToken cancellationToken = default );
	}
}
