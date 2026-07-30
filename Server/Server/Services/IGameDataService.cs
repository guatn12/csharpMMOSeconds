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

		/// <summary>
		/// 플레이어 + 인벤토리 데이터를 통합 저장
		/// 반드시 LoadPlayerAggregateAsync() 로드 후 SavePlayerAggregateAsync() 저장해야 함
		/// new PlayerEntity() 생성 후 SavePlayerAggregateAsync() 저장 시, 인벤토리 데이터가 없으면 인벤토리 테이블에 null 값이 들어감
		/// </summary>
		Task<bool> SavePlayerAggregateAsync( PlayerAggregate aggregate );

		Task<List<PlayerEntity>> GetPlayerListByAccountIdAsync( long accountId );
		Task<bool> IsPlayerNameTakenAsync( string playerName );
		Task<bool> IsPlayerOwnedByAccountAsync( long accountId, long playerId );

		/// <summary>
		/// 신규 플레이어 데이터를 DB에 생성.
		/// </summary>
		Task<PlayerAggregate> CreateNewPlayerAsync( string playerName, long accountId );

	}

	public sealed class PlayerAggregate
	{
		public required PlayerEntity Player { get; init; }
		public InventoryEntity Inventory { get; init; }
		public EquipmentEntity Equipment { get; init; }
	}
}
