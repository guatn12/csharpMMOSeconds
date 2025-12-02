using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
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
	/// 아이템 사용 패킷 핸들러
	/// </summary>
	public class UseItemPacketHandler : PacketHandlerBase<C_UseItem, S_UseItem>
	{
		#region 내부 DTO
		/// <summary>
		/// 아이템 사용 처리 결과 DTO
		/// 현재: 내부 클래스로 사용
		/// 향후: Service Layer 도입 시 DTOs/UseItemResult.cs로 이동 가능.
		/// </summary>
		public class UseItemResult
		{
			public int ItemId { get; set; }
			public int RemainingQuantity { get; set; }
			public bool HPChanged { get; set; }
			public bool MPChanged { get; set; }
			public int HPRecovered { get; set; }
			public int MPRecovered { get; set; }
		}
		#endregion

		#region 생성자
		public UseItemPacketHandler( BaseRoom room )
			: base( room, validators: null, responder: new SendToPlayerResponder<S_UseItem>() )
		{ }
		#endregion

		#region Virtual 메서드 정의 (선택)
		/// <summary>
		/// Validator 체인 구성
		/// Priority 순서: BasicPacketValidator(0) -> ItemSlotValidator(10)
		/// </summary>
		protected override IEnumerable<IPacketValidator<C_UseItem>> GetDefaultValidators()
		{
			return new List<IPacketValidator<C_UseItem>>
			{
				new BasicPacketValidator<C_UseItem>(),
				new ItemSlotValidator()
			};
		}

		/// <summary>
		/// 성공 후처리: HP/MP 변경 시 주변에 브로드캐스트
		/// </summary>
		/// <param name="session"></param>
		/// <param name="packet"></param>
		/// <param name="result"></param>
		/// <param name="logger"></param>
		/// <returns></returns>
		protected override async Task OnSuccessAsync( GameSession session, C_UseItem packet, PacketProcessResult result, ILogger logger )
		{
			var data = result.Data as UseItemResult;

			// HP 또는 MP가 변경되었으면 주변에 알림
			if(data?.HPChanged == true ||  data?.MPChanged == true)
			{
				var updatePacket = new S_PlayerUpdate
				{
					Player = session.Player.Info
				};

				// 자신을 제외하고 주변에 브로드캐스트
				await Room.BroadcastAsync( updatePacket, session );

				logger.LogDebug( "플레이어 상태 업데이트 브로드캐스트: Session={SessionId}, HP={HP}, MP={MP}",
					session.SessionId, session.Player.CurrentHP, session.Player.CurrentMP );
			}
		}

		/// <summary>
		/// 성공 로드
		/// </summary>
		protected override void LogSuccess( GameSession session, C_UseItem packet, ILogger logger )
		{
			logger.LogDebug( "UseItem 패킷 처리 완료: Session={SessionId}, Slot={Slot}, Quantity={Quantity}",
				session.SessionId, packet.Slot, packet.Quantity );
		}
		#endregion

		#region Abstract 메서드 정의 (필수)
		/// <summary>
		/// 핵심 비즈니스 로직 : 아이템 사용
		/// </summary>
		protected override Task<PacketProcessResult> ProcessPacketAsync( GameSession session, C_UseItem packet, ILogger logger )
		{
			// 1. 인벤토리에서 아이템 조회
			var item = session.Player.Inventory.GetItem(packet.Slot);
			if(item == null)
			{
				logger.LogDebug( "아이템 없음: Session={SessionId}, Slot={Slot}", session.SessionId, packet.Slot );

				return Task.FromResult( PacketProcessResult.Fail( "해당 슬롯에 아이템이 없습니다." ) );
			}

			// 2. 보유 수량 검증
			if(item.Quantity < packet.Quantity)
			{
				logger.LogDebug( "수량 부족: Session={SessionId}, 요청={RequestedQty}, 보유={CurrentQty}",
					session.SessionId, packet.Quantity, item.Quantity );

				return Task.FromResult( PacketProcessResult.Fail( $"수량이 부족합니다. (보유:{item.Quantity})" ) );
			}

			// 3. 상태 변경 전 캡처 (OnSuccessAsync에서 사용)
			int oldHP = session.Player.CurrentHP;
			int oldMP = session.Player.CurrentMP;

			// 4. 실제 아이템 사용
			bool success = session.Player.UseItem(packet.Slot, packet.Quantity);
			if(!success)
			{
				logger.LogWarning( "아이템 사용 실패: Session={SessionId}, ItemId={ItemId}, Slot={Slot}",
				  session.SessionId, item.ItemId, packet.Slot );

				return Task.FromResult( PacketProcessResult.Fail( "아이템 사용에 실패했습니다." ) );
			}

			// 5. 결과 데이터 생성
			var resultData = new UseItemResult
			{
				ItemId = item.ItemId,
				RemainingQuantity = item.Quantity,
				HPChanged = oldHP != session.Player.CurrentHP,
				MPChanged = oldMP != session.Player.CurrentMP,
				HPRecovered = session.Player.CurrentHP - oldHP,
				MPRecovered = session.Player.CurrentMP - oldMP,
			};

			logger.LogInformation(
			 "아이템 사용 성공: Session={SessionId}, ItemId={ItemId}, HP회복={HPRecovered}, MP회복={MPRecovered}",
			 session.SessionId, item.ItemId, resultData.HPRecovered, resultData.MPRecovered );

			return Task.FromResult( PacketProcessResult.Ok( resultData ) );
		}

		/// <summary>
		/// 응답 패킷 생성
		/// </summary>
		protected override Task<S_UseItem> BuildResponseAsync( GameSession session, C_UseItem packet, PacketProcessResult result, ILogger logger )
		{
			var data = result.Data as UseItemResult;

			var response = new S_UseItem
			{
				Success = true,
				Slot = packet.Slot,
				Message = "아이템 사용 성공",
				RemainingQuantity = data?.RemainingQuantity ?? 0,
			};

			return Task.FromResult( response );
		}
		#endregion
	}
}
