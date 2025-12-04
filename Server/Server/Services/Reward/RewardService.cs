using Microsoft.Extensions.Logging;
using Server.Game;
using Server.Game.Monsters;
using Server.Services.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.Reward
{
	public class RewardService : IRewardService
	{
		private readonly ILogger<RewardService> _logger;

		public RewardService(ILogger<RewardService> logger)
		{
			_logger = logger;
		}

		public async Task<RewardInfo> CalculateMonsterRewardAsync( Player player, Monster monster )
		{
			RewardInfo reward = new RewardInfo();

			// 경험치 계산
			reward.Experience = monster.StaticData.ExpReward;

			// 골드 계산
			reward.Gold = monster.StaticData.GoldReward;

			// 아이템 드롭 계산 (30%)
			if(Random.Shared.NextDouble() < 0.3)
			{
				int itemId = 1001;
				int quantity = 1;

				reward.DroppedItem.Add( new Protocol.InventoryItemInfo
				{
					ItemId = itemId,
					Quantity = quantity,
					Slot = -1
				} );

				_logger.LogDebug(
					  "Item {ItemId} dropped from Monster {MonsterId}",
					  itemId, monster.MonsterId );
			}

			_logger.LogInformation(
				  "Calculated reward for Monster {MonsterId}: Exp={Exp}, Gold={Gold}, Items={ItemCount}",
				  monster.MonsterId, reward.Experience, reward.Gold, reward.DroppedItem.Count
			  );

			return await Task.FromResult( reward );
		}

		public Task GiveRewardAsync( Player player, RewardInfo reward )
		{
			// 경험치 지급
			bool leveledUp = player.GainExperience(reward.Experience);

			if( leveledUp )
			{
				reward.LeveledUp = true;
				reward.NewLevel = player.Level;
				reward.NewMaxMP = player.MaxMP;
				reward.NewMaxHP = player.MaxHP;

				_logger.LogInformation( "Player {PlayerId} leveled up to {Level}", player.PlayerId, player.Level );
			}

			// 골드 지급
			player.AddGold( reward.Gold );

			// 아이템 지급
			foreach( var item in reward.DroppedItem )
			{
				bool added = player.AddItem(item.ItemId, item.Quantity);
				if(!added)
				{
					_logger.LogWarning( "Failed to add item {ItemId} to player {PlayerId} inventory( full )",
						item.ItemId, player.PlayerId );
				}
			}

			_logger.LogInformation( "Gave reward to Player {PlayerId}: Exp={Exp}, Gold={Gold}, LevelUp={LevelUp}",
				 player.PlayerId, reward.Experience, reward.Gold, leveledUp );

			return Task.CompletedTask;
		}
	}
}
