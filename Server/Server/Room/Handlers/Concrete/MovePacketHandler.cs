using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Room.Handlers.Implementations;
using Server.Room.Handlers.Strategies;
using Server.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Handlers.Concrete
{
	/// <summary>
	/// 이동 패킷 핸들러
	/// 본인 제외의 이유
	/// - 클라이언트가 이미 예측 이동 처리.
	/// - 서버 응답은 다른 플레이어에게만 필요
	/// - 중복 전송하면 화면 버벅임.?
	/// </summary>
	public class MovePacketHandler : PacketHandlerBase<C_Move, S_Move>
	{
		private readonly PlayerPositionService _playerPositionService;

		#region 생성자
		public MovePacketHandler( BaseRoom room, PlayerPositionService playerPositionService )
			: base( room,
				  validators: null, responder: new BroadcastResponder<S_Move>( includeSelf: false ) )
		{
			_playerPositionService = playerPositionService;
		}
		#endregion

		#region virtual 메서드 재정의 (선택)
		/// <summary>
		/// 기본 Validator 목록 재정의
		/// </summary>
		/// <returns></returns>
		protected override IEnumerable<IPacketValidator<C_Move>> GetDefaultValidators()
		{
			return new List<IPacketValidator<C_Move>>
			{
				new BasicPacketValidator<C_Move>(),
				new MoveRangeValidator()
			};
		}

		/// <summary>
		/// 성공 후 처리: 룸별 이동 처리
		/// </summary>
		protected override async Task OnSuccessAsync( GameSession session, C_Move packet, PacketProcessResult result, ILogger logger )
		{
			// 룸별 이동 처리
			await Room.OnPlayerMoveAsync( session, packet );
		}

		/// <summary>
		/// Validation 실패 처리 재정의
		/// 이동 실패 시 
		/// - 현재 위치를 클라이언트에 재전송
		/// - 클라이언트가 잘못된 위치로 이동한 경우 교정
		/// </summary>
		protected override async Task HandleValidationFailureAsync( GameSession session, ValidationResult validationResult, ILogger logger )
		{
			logger.LogWarning("이동 검증 실패: Session={SessionId}, Reason={Reason}", 
				session.SessionId, validationResult.ErrorMessage );

			// 현재 위치를 클라이언트에 재전송(동기화)
			PosInfo currentPos = await _playerPositionService.GetPositionAsync(session.PlayerId);
			if(currentPos != null)
			{
				var correctionPacket = new S_Move
				{
					PlayerId = session.PlayerId,
					PosInfo = currentPos
				};
				await Room.SendToPlayerAsync( session, correctionPacket );

				logger.LogInformation("위치 동기화 완료: Session={SessionId}, Pos=({X}, {Y}, {Z})",
					session.SessionId, currentPos.PosX, currentPos.PosY,  currentPos.PosZ );
			}
		}
		#endregion

		#region Abstract 메서드 구현 (필수)
		
		/// <summary>
		/// 핵심 비즈니스 로직 : 위치 업데이트
		/// 1. Redis에 새 위치 저장
		/// 2. 저장 성공 여부 확인
		/// 3. 성공 시 PosInfo 반환
		/// </summary>
		protected override async Task<PacketProcessResult> ProcessPacketAsync( GameSession session, C_Move packet, ILogger logger )
		{
			try
			{
				// Redis 기반 3D 위치 업데이트
				await _playerPositionService.UpdatePositionAsync(session.PlayerId, packet.PosInfo);

				// 성공 posinfo를 data로 전달.
				return PacketProcessResult.Ok( packet.PosInfo );
			}
			catch( Exception ex )
			{
				logger.LogWarning( "위치 업데이트 실패: Session={SessionId}", session.SessionId );
				return PacketProcessResult.Fail( "위치 업데이트에 실패했습니다." );
			}
		}

		/// <summary>
		/// 응답 패킷 생성
		/// </summary>
		protected override Task<S_Move> BuildResponseAsync( GameSession session, C_Move packet, PacketProcessResult result, ILogger logger )
		{
			var response = new S_Move
			{
				PlayerId = session.PlayerId,
				PosInfo= packet.PosInfo,
			};

			return Task.FromResult(response);
		}
		#endregion

		#region 커스텀 Validator
		public class MoveRangeValidator : IPacketValidator<C_Move>
		{
			/// <summary>
			/// 우선 순위 15(BasicValidator 다음)
			/// </summary>
			public int Priority => 15;

			/// <summary>
			/// 3D 범위 검증
			/// </summary>
			public Task<ValidationResult> ValidateAsync(GameSession session, C_Move packet, BaseRoom room, ILogger logger)
			{
				// PosInfo null 체크
				if(packet.PosInfo == null)
				{
					return Task.FromResult( ValidationResult.Failure( "위치 정보가 없습니다." ) );
				}

				var pos = packet.PosInfo;

				// x축 범위 체크
				if(pos.PosX < room.MinX || room.MaxX < pos.PosX)
				{
					logger.LogWarning("X축 범위 초과: Session={SessionId}, X={X}, Range=[{MinX}, {MaxX}]",
						session.SessionId, pos.PosX, room.MinX, room.MaxX );
					return Task.FromResult( ValidationResult.Failure( "X축 이동 범위를 벗어났습니다." ) );
				}


				// y축 범위 체크
				if(pos.PosY < room.MinY || room.MaxY < pos.PosY)
				{
					logger.LogWarning( "Y축 범위 초과: Session={SessionId}, Y={Y}, Range=[{MinY}, {MaxY}]",
						session.SessionId, pos.PosY, room.MinY, room.MaxY );
					return Task.FromResult( ValidationResult.Failure( "Y축 이동 범위를 벗어났습니다." ) );
				}


				// z축 범위 체크
				if(pos.PosZ < room.MinZ || room.MaxZ < pos.PosZ)
				{
					logger.LogWarning( "Z축 범위 초과: Session={SessionId}, Z={Z}, Range=[{MinZ}, {MaxZ}]",
						session.SessionId, pos.PosZ, room.MinZ, room.MaxZ );
					return Task.FromResult( ValidationResult.Failure( "Z축 이동 범위를 벗어났습니다." ) );
				}

				// 모든 범위 체크 통과.
				return Task.FromResult( ValidationResult.Success() );
			}
		}
		#endregion
	}
}
