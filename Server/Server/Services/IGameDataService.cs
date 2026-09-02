using DatabaseLib.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services
{
	/// <summary>
	/// 플레이어 데이터 통합 로드/저장 서비스
	/// Redis 우선 조회(cache-aside) + DB 풀백
	/// </summary>
	public interface IGameDataService
	{
		/// <summary>
		/// 플레이어 + 인벤토리 데이터를 통합 로드
		/// </summary>
		Task<PlayerAggregate> LoadPlayerAggregateAsync( long accountId, long playerId );

		Task<List<PlayerEntity>> GetPlayerListByAccountIdAsync( long accountId );
		Task<bool> IsPlayerNameTakenAsync( string playerName );
		Task<bool> IsPlayerOwnedByAccountAsync( long accountId, long playerId );
	}

	public sealed class PlayerAggregate
	{
		public required PlayerEntity Player { get; init; }
		public PlayerStateEntity PlayerState { get; init; }
		public InventoryEntity Inventory { get; init; }
		public EquipmentEntity Equipment { get; init; }
	}
}
