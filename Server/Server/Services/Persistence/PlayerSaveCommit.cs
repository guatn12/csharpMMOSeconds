using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.Persistence
{
	public sealed record PlayerSaveCommit( long PlayerRawId, long SavedRevision, long InventoryVersion,
		long EquipmentVersion, long PlayerStateVersion );

	public sealed record CreatePlayerCommit( long PlayerRawId, long InventoryId, long EquipmentId, long PlayerStateId );
}
