using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services
{
	public interface IPlayerPositionService
	{
		Task UpdatePositionAsync( long playerId, PosInfo posInfo );
		Task<PosInfo> GetPositionAsync( long playerId );
		Task<List<(long PlayerId, PosInfo Position)>> GetNearByPlayersAsync( float centerX, float centerY, float centerZ, float radius );
		Task RemovePositionAsync( long playerId );
		float CalculateDistance3D( PosInfo pos1, PosInfo pos2 );
		Task<bool> UpdatePositionWithValidationAsync( long playerId, PosInfo posInfo, Room.BaseRoom room );
		Task<List<(long PlayerId, PosInfo Position)>> GetNearbyPlayersInRoomAsync(
			long playerId, float radius, Room.BaseRoom room, int maxResults = 50 );
	}
}
