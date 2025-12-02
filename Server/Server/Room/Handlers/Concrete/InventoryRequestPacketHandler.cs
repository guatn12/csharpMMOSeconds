using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Extensions;
using Server.Room.Handlers.Implementations;
using Server.Room.Handlers.Strategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Handlers.Concrete
{
	/// <summary>
	/// 인벤토리 조회 패킷 핸들러
	/// </summary>
	public class InventoryRequestPacketHandler : PacketHandlerBase<C_InventoryRequest, S_InventoryData>
	{
		#region 생성자
		public InventoryRequestPacketHandler(BaseRoom room)
			: base(room, validators: null, responder: new SendToPlayerResponder<S_InventoryData>()) { }
		#endregion

		#region virtual 메서드 재정의 (선택)

		/// <summary>
		/// Validator 체인 구성.
		/// </summary>
		protected override IEnumerable<IPacketValidator<C_InventoryRequest>> GetDefaultValidators()
		{
			return new List<IPacketValidator<C_InventoryRequest>>
			{
				new BasicPacketValidator<C_InventoryRequest>()
			};
		}

		protected override void LogSuccess( GameSession session, C_InventoryRequest packet, ILogger logger )
		{
			logger.LogDebug( "InventoryRequest 패킷 처리 완료: Session={SessionId}", session.SessionId );
		}
		#endregion

		#region Abstract 메서드 구현 (필수)

		/// <summary>
		/// 응답 패킷 생성: S_InventoryData
		/// </summary>
		protected override Task<S_InventoryData> BuildResponseAsync( GameSession session, C_InventoryRequest packet, PacketProcessResult result, ILogger logger )
		{
			var response = new S_InventoryData
			{
				Gold = session.Player.Inventory.Gold,
				MaxSlots = session.Player.Inventory.MaxSlots,
			};

			// 인벤토리 아이템 목록 추가
			var items = session.Player.Inventory.GetAllItems();
			if(items != null && 0 < items.Count)
			{
				var protoItems = items.Select(item => item.ToProto());
				response.Items.AddRange( protoItems );
			}

			logger.LogDebug( "S_InventoryData 생성: Session={SessionId}, Items={ItemCount}, Gold={Gold}, MaxSlots={MaxSlots}",
				session.SessionId, response.Items.Count, response.Gold, response.MaxSlots);

			return Task.FromResult( response );
		}

		/// <summary>
		/// 비즈니스 로직 처리: 인벤토리 전체 조회
		/// </summary>
		protected override Task<PacketProcessResult> ProcessPacketAsync( GameSession session, C_InventoryRequest packet, ILogger logger )
		{
			if(session.Player?.Inventory == null)
			{
				logger.LogWarning( "인벤토리 정보 없음: Session={SessionId}", session.SessionId );

				return Task.FromResult( PacketProcessResult.Fail( "인벤토리 정보가 존재하지 않습니다." ) );
			}

			// 인벤토리 전체 데이터 로깅
			int itemCount = session.Player.Inventory.GetAllItems().Count;
			int maxSlots = session.Player.Inventory.MaxSlots;
			long gold  = session.Player.Inventory.Gold;

			logger.LogInformation("인벤토리 전체 조회: Session={SessionId}, ItemCount={ItemCount}, Gold={Gold}, MaxSlots={MaxSlots}",
				session.SessionId, itemCount, gold, maxSlots);

			// 단순 조회는 Data 없이 Success 반환
			return Task.FromResult( PacketProcessResult.Ok() );
		}
		#endregion
	}
}
