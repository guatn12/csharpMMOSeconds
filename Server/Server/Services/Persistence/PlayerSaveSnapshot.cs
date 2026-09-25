using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.Persistence
{
	public sealed record PlayerSaveSnapshot( long PlayerRawId, long AccountId, long Revision, string PlayerName, int Level, long Experience,
		long TotalPlayTimeMinutes, string PlayerSettingsJson, long InventoryId, long InventoryVersion, int InventoryMaxSlots,
		string InventoryDataJson, long EquipmentId, long EquipmentVersion, string EquipmentDataJson, long PlayerStateId,
		long PlayerStateVersion, string PlayerStateDataJson, DateTime? LastEnteredGameAt );
}
