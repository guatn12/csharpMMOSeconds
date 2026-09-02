using Server.Services;
using Server.Services.Combat;
using Server.Services.Persistence;
using Server.Services.Reward;

namespace Server.Room.Dependencies
{
	public sealed class RoomServices
	{
		public ICombatService CombatService { get; }
		public IRewardService RewardService { get; }
		public IPlayerPositionService PlayerPositionService { get; }
		public IPlayerPersistenceService PlayerPersistenceService { get; }

		public RoomServices(ICombatService combatService, IRewardService rewardService, IPlayerPositionService playerPositionService,
			IPlayerPersistenceService playerPersistenceService)
		{
			CombatService = combatService;
			RewardService = rewardService;
			PlayerPositionService = playerPositionService;
			PlayerPersistenceService = playerPersistenceService;
		}
	}
}
