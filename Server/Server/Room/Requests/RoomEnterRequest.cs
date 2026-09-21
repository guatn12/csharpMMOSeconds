using Protocol;
using Server.Core.Session;

namespace Server.Room.Requests
{
	public sealed class RoomEnterRequest
	{
		public required IClientSession Session { get; init; }
		public int? SavedMapId { get; init; }
		public PosInfo PreferredPosition { get; init; }

		public bool IsInitialGameEntry { get; init; } = false;

		public bool HasLoginRecovery => SavedMapId.HasValue && PreferredPosition != null;
	}
}
