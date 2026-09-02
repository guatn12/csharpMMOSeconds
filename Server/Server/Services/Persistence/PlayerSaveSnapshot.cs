using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.Persistence
{
	public sealed record PlayerSaveSnapshot( long PlayerRawId, long AccountId, long Revision, string PlayerName, int Level, long Experience,
		long TotalPlayTimeMinutes, string PlayerSettingsJson, long InventoryId, int InventoryVersion, int InventoryMaxSlots,
		string InventoryDataJson, long EquipmentId, int EquipmentVersion, string EquipmentDataJson, long PlayerStateId,
		int PlayerStateVersion, string PlayerStateDataJson );
}
