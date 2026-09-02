using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.Persistence
{
	public sealed record PlayerSaveCommit( long PlayerRawId, long SavedRevision, int InventoryVersion,
		int EquipmentVersion, int PlayerStateVersion );

	public sealed record CreatePlayerCommit( long PlayerRawId, long InventoryId, long EquipmentId, long PlayerStateId );
}
