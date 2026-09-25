using DatabaseLib.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.Persistence
{
	public sealed class PlayerPersistenceState
	{
		public long AccountId { get; private set; }
		public long InventoryId { get; private set; }
		public long InventoryDbVersion { get; private set; }
		public long EquipmentId { get; private set; }
		public long EquipmentDbVersion { get; private set; }
		public long PlayerStateId { get; private set; }
		public long PlayerStateDbVersion { get; private set; }
		
		public long DirtyRevision { get; private set; }
		public long CleanRevision { get; private set; }
		public bool RequiresReload { get; private set; }
		public bool IsDirty => CleanRevision < DirtyRevision;

		public Task<DatabaseWriteResult<PlayerSaveCommit>> PendingSave { get; private set; }

		public void ResetAfterLoad(long accountId, long inventoryId, long inventoryDbVersion, 
			long equipmentId, long equipmentDbVersion, long playerStateId, long  playerStateDbVersion)
		{
			AccountId = accountId;
			InventoryId = inventoryId;
			InventoryDbVersion = inventoryDbVersion;
			EquipmentId = equipmentId;
			EquipmentDbVersion = equipmentDbVersion;
			PlayerStateId = playerStateId;
			PlayerStateDbVersion = playerStateDbVersion;

			DirtyRevision = 0;
			CleanRevision = 0;
			PendingSave = null;
			RequiresReload = false;
		}

		public void MarkDirty()
		{
			checked
			{
				DirtyRevision++;
			}
		}

		public void MarkRequiresReload()
		{
			RequiresReload = true;
		}

		public void SetPending(Task<DatabaseWriteResult<PlayerSaveCommit>> task)
		{
			if(task == null)
				throw new ArgumentNullException( nameof( task ) );

			if(PendingSave != null)
				throw new InvalidOperationException( "Player save is already pending." );

			PendingSave = task;
		}

		public void ClearPending(Task<DatabaseWriteResult<PlayerSaveCommit>> task)
		{
			if(ReferenceEquals( PendingSave, task ))
				PendingSave = null;
		}

		public void ApplyCommitted(PlayerSaveCommit commit)
		{
			InventoryDbVersion = commit.InventoryVersion;
			EquipmentDbVersion = commit.EquipmentVersion;
			PlayerStateDbVersion = commit.PlayerStateVersion;
			CleanRevision = Math.Max( CleanRevision, commit.SavedRevision );
		}
	}
}
