using DatabaseLib;
using DatabaseLib.Entities;
using DatabaseLib.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Services.Persistence
{
	public sealed class CreatePlayerCommand : IDatabaseWriteCommand<CreatePlayerCommit>
	{
		private readonly long _accountId;
		private readonly string _playerName;
		private PlayerEntity _player;
		private InventoryEntity _inventory;
		private EquipmentEntity _equipment;
		private PlayerStateEntity _playerState;

		public CreatePlayerCommand(long accountId, string playerName)
		{
			_accountId = accountId;
			_playerName = playerName ?? throw new ArgumentNullException(nameof(playerName));
		}

		public string Name => "CreatePlayer";

		public async ValueTask<DatabaseWriteDecision> ApplyAsync(AppDbContext context, CancellationToken cancellationToken)
		{
			bool duplicated = await context.Players.AsNoTracking().AnyAsync(x => x.PlayerName == _playerName, cancellationToken);
			if(duplicated)
				return DatabaseWriteDecision.Reject( "PlayerNameTaken" );

			DateTime now = DateTime.UtcNow;

			_player = new PlayerEntity
			{
				AccountId = _accountId,
				PlayerName = _playerName,
				Level = 1,
				Experience = 0,
				PlayerSettings = new PlayerSettingsModel(),
				CreatedAt = now,
				UpdatedAt = now,
			};

			_inventory = new InventoryEntity
			{
				Player = _player,
				MaxSlots = 50,
				Version = 1,
				InventoryDataJson = "{}",
				CreatedAt = now,
				LastUpdated = now
			};

			_equipment = new EquipmentEntity
			{
				Player = _player,
				Version = 1,
				EquipmentDataJson = "{}",
				CreatedAt = now,
				LastUpdated = now
			};

			_playerState = new PlayerStateEntity
			{
				Player = _player,
				Version = 1,
				StateData = new PlayerStateModel
				{
					MapId = 0,
					CurrentHp = 100,
					CurrentMp = 100,
					CreatureState = 0
				},
				CreatedAt = now,
				LastUpdated = now
			};

			context.AddRange( _player, _inventory, _equipment, _playerState );
			return DatabaseWriteDecision.Commit();
		}

		public CreatePlayerCommit BuildResultAfterSave()
		{
			return new CreatePlayerCommit( _player.PlayerId, _inventory.InventoryId, _equipment.EquipmentId, _playerState.PlayerStateId );
		}
	}
}
