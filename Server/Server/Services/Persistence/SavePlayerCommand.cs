using DatabaseLib;
using DatabaseLib.Entities;
using DatabaseLib.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Services.Persistence
{
	public sealed class SavePlayerCommand : IDatabaseWriteCommand<PlayerSaveCommit>
	{
		private readonly PlayerSaveSnapshot _snapshot;
		private InventoryEntity _inventory;
		private EquipmentEntity _equipment;
		private PlayerStateEntity _playerState;

		public SavePlayerCommand(PlayerSaveSnapshot snapshot)
		{
			_snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
		}

		public string Name => "SavePlayer";

		public async ValueTask<DatabaseWriteDecision> ApplyAsync(AppDbContext context,CancellationToken cancellationToken)
		{
			PlayerEntity player = await context.Players.SingleOrDefaultAsync(x => x.PlayerId == _snapshot.PlayerRawId, cancellationToken);
			_inventory = await context.Inventory.SingleOrDefaultAsync( x => x.InventoryId == _snapshot.InventoryId && x.PlayerId == _snapshot.PlayerRawId, cancellationToken );
			_equipment = await context.Equipment.SingleOrDefaultAsync( x => x.EquipmentId == _snapshot.EquipmentId && x.PlayerId == _snapshot.PlayerRawId, cancellationToken );
			_playerState = await context.PlayerState.SingleOrDefaultAsync( x => x.PlayerStateId == _snapshot.PlayerStateId && x.PlayerId == _snapshot.PlayerRawId, cancellationToken );

			if(player == null || _inventory == null || _equipment == null || _playerState == null)
				return DatabaseWriteDecision.Reject( "PlayerAggregateMissing" );

			if(player.AccountId != _snapshot.AccountId)
				return DatabaseWriteDecision.Reject( "PlayerAccountMismatch" );

			DateTime now = DateTime.UtcNow;

			player.PlayerName = _snapshot.PlayerName;
			player.Level = _snapshot.Level;
			player.Experience = _snapshot.Experience;
			player.TotalPlayTimeMinutes = _snapshot.TotalPlayTimeMinutes;
			player.PlayerSettingsJson = _snapshot.PlayerSettingsJson;
			player.UpdatedAt = now;
			player.LastEnteredGameAt = _snapshot.LastEnteredGameAt;

			_inventory.MaxSlots = _snapshot.InventoryMaxSlots;
			_inventory.InventoryDataJson = _snapshot.InventoryDataJson;
			_inventory.LastUpdated = now;
			SetExpectedVersion( context.Entry( _inventory ), _snapshot.InventoryVersion );

			_equipment.EquipmentDataJson = _snapshot.EquipmentDataJson;
			_equipment.LastUpdated = now;
			SetExpectedVersion( context.Entry( _equipment ), _snapshot.EquipmentVersion );

			_playerState.StateDataJson = _snapshot.PlayerStateDataJson;
			_playerState.LastUpdated = now;
			SetExpectedVersion( context.Entry( _playerState ), _snapshot.PlayerStateVersion );

			return DatabaseWriteDecision.Commit();
		}

		public PlayerSaveCommit BuildResultAfterSave()
		{
			return new PlayerSaveCommit( _snapshot.PlayerRawId, _snapshot.Revision, _inventory.Version, _equipment.Version, _playerState.Version );
		}

		private static void SetExpectedVersion<TEntity>(EntityEntry<TEntity> entry, long expectedVersion) where TEntity : class
		{
			var version = entry.Property<long>("Version");
			version.OriginalValue = expectedVersion;
			version.CurrentValue = checked(expectedVersion + 1);
		}
	}
}
