using Server.Game;
using Server.Services.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services.Reward
{
	/// <summary>
	/// 보상 처리를 담당하는 Service 인터페이스
	/// </summary>
	public interface IRewardService
	{
		/// <summary>
		/// 몬스터 처치 보상을 계산합니다.
		/// </summary>
		/// <param name="player"></param>
		/// <param name="monster"></param>
		/// <returns></returns>
		Task<RewardInfo> CalculateMonsterRewardAsync( Player player, Monster monster );

		/// <summary>
		/// 보상을 플레이어에게 지급합니다.
		/// </summary>
		/// <param name="player"></param>
		/// <param name="reward"></param>
		/// <returns></returns>
		Task GiveRewardAsync( Player player, RewardInfo reward );
	}
}
