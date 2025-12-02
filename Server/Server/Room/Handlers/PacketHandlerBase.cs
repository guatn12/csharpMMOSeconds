using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Server.Core.Session;
using Server.Room.Handlers.Strategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Handlers
{
	/// <summary>
	/// 패킷 핸들러 기본 클래스
	/// </summary>
	public abstract class PacketHandlerBase<TRequest, TResponse> where TRequest : IMessage where TResponse : IMessage, new()
	{
		#region 필드 및 속성
		/// <summary>
		/// 현재 룸 (패킷 처리 컨텍스트)
		/// </summary>
		protected readonly BaseRoom Room;

		/// <summary>
		/// Validator 체인 (우선순위 순으로 정렬됨)
		/// 
		/// Chain of Responsibility Pattern:
		/// - 여러 Validator를 순차 실행
		/// - 하나라도 실패하면 체인 중단
		/// - Priority 낮은 순서대로 실행.
		/// </summary>
		protected readonly List<IPacketValidator<TRequest>> Validators;

		/// <summary>
		/// 응답 전송 전략
		/// </summary>
		protected readonly IPacketResponder<TResponse> Responder;
		#endregion

		#region 생성자
		/// <summary>
		/// 생성자
		/// </summary>
		/// <param name="room"></param>
		/// <param name="validators"></param>
		/// <param name="Responder"></param>
		/// <exception cref="ArgumentNullException"></exception>
		protected PacketHandlerBase(BaseRoom room, IEnumerable<IPacketValidator<TRequest>> validators = null,
			IPacketResponder<TResponse> responder = null)
		{
			Room = room ?? throw new ArgumentNullException(nameof(room));
			Validators = (validators ?? GetDefaultValidators())
				.OrderBy( v => v.Priority ) // 우선 순위 낮은 순
				.ToList();

			// null이면 기본값 SendToPlayer
			Responder = responder ?? new Implementations.SendToPlayerResponder<TResponse>();
		}

		/// <summary>
		/// 기본 Validator 목록 (하위 클래스에서 재정의 가능)
		/// </summary>
		/// <returns></returns>
		protected virtual IEnumerable<IPacketValidator<TRequest>> GetDefaultValidators()
		{
			return new List<IPacketValidator<TRequest>>
			{
				new Implementations.BasicPacketValidator<TRequest>()
			};
		}
		#endregion

		#region Template Method (알고리즘 골격)
		/// <summary>
		/// 패킷 처리 진입점 - Template Method
		/// </summary>
		/// <param name="session"></param>
		/// <param name="packet"></param>
		/// <param name="logger"></param>
		/// <returns></returns>
		public async Task HandleAsync(GameSession session, TRequest packet, ILogger logger)
		{
			try
			{
				// 1. Validator Chain 실행
				foreach(var validator in Validators)
				{
					var validationResult = await validator.ValidateAsync(session, packet, Room, logger);
					if (!validationResult.IsValid)
					{
						await HandleValidationFailureAsync( session, validationResult, logger );
						return;
					}
				}

				// 2. 핵심 비즈니스 로직 실행
				var result = await ProcessPacketAsync(session, packet, logger);
				if(!result.Success)
				{
					await HandleProcessFailureAsync( session, result, logger );
					return;
				}

				// 3. 응답 패킷 생성
				var response = await BuildResponseAsync(session, packet, result, logger);

				// 4. 응답 전송 (Strategy 사용)
				await Responder.SendAsync( session, response, Room, logger );

				// 5. 성공 후처리
				await OnSuccessAsync( session, packet, result, logger );

				// 6. 성공 로깅
				LogSuccess( session, packet, logger );
			}
			catch ( Exception ex )
			{
				// 7. 예외 처리
				await HandleErrorAsync( session, packet, ex, logger );
			}
		}
		#endregion

		#region Virtual Methods (하위 클래스에서 재정의 가능)
		/// <summary>
		/// Validation 실패 처리
		/// </summary>
		/// <param name="session"></param>
		/// <param name="validationResult"></param>
		/// <param name="logger"></param>
		/// <returns></returns>
		protected virtual async Task HandleValidationFailureAsync(GameSession session, ValidationResult validationResult,
			ILogger logger)
		{
			logger.LogWarning( "검증 실패: Session={SessionId}, Reason={Reason}",
				session.SessionId, validationResult.ErrorMessage );

			// 기본 에러 응답 전송
			var errorResponse = CreateErrorResponse(validationResult.ErrorMessage);

			await Room.SendToPlayerAsync( session, errorResponse );
		}

		/// <summary>
		/// 비즈니스 로직 처리 실패
		/// </summary>
		/// <param name="session"></param>
		/// <param name="result"></param>
		/// <param name="logger"></param>
		/// <returns></returns>
		protected virtual async Task HandleProcessFailureAsync(GameSession session, PacketProcessResult result, ILogger logger)
		{
			logger.LogWarning( "처리 실패: Session={SessionId}, Reason={Reason}",
				session.SessionId, result.Message );

			// 기본 에러 응답 전송
			var errorResponse = CreateErrorResponse(result.Message);

			await Room.SendToPlayerAsync( session, errorResponse );
		}

		/// <summary>
		/// 성공 후처리 (브로드캐스트, 상태 업데이트 등)
		/// 하위 클래스에서 필요 시 재정의(기본 구현은 아무것도 안함)
		/// </summary>
		/// <param name="session"></param>
		/// <param name="packet"></param>
		/// <param name="result"></param>
		/// <param name="logger"></param>
		/// <returns></returns>
		protected virtual Task OnSuccessAsync(GameSession session, TRequest packet, PacketProcessResult result, ILogger logger)
		{
			return Task.CompletedTask;
		}

		/// <summary>
		/// 성공 로깅
		/// </summary>
		protected virtual void LogSuccess(GameSession session, TRequest packet, ILogger logger)
		{
			logger.LogDebug( "패킷 처리 성공: Session={SessionId}, Room={RoomId}, Packet={PacketType}",
				session.SessionId, Room.RoomId, typeof( TRequest ).Name);
		}

		/// <summary>
		/// 에러 응답 생성(하위 클래스에서 재정의) 
		/// 기본 구현은 빈응답.
		/// </summary>
		/// <param name="errorMessage"></param>
		/// <returns></returns>
		protected virtual TResponse CreateErrorResponse(string errorMessage)
		{
			return new TResponse();
		}

		/// <summary>
		/// 예외 처리
		/// </summary>
		/// <param name="session"></param>
		/// <param name="packet"></param>
		/// <param name="ex"></param>
		/// <param name="logger"></param>
		/// <returns></returns>
		protected virtual async Task HandleErrorAsync(GameSession session, TRequest packet, Exception ex, ILogger logger)
		{
			logger.LogError( ex, "패킷 처리 예외: Session={SessionId}, Room={RoomId},Packet={PacketType}",
				session.SessionId, Room.RoomId, typeof( TRequest ).Name);

			var errorResponse = CreateErrorResponse("서버 오류가 발생했습니다.");
			await Room.SendToPlayerAsync(session, errorResponse );
		}
		#endregion

		#region Abstract Methods (하위 필수 구현)

		/// <summary>
		/// 핵심 비즈니스 로직 (하위 클래스 필수 구현)
		/// </summary>
		/// <param name="session"></param>
		/// <param name="packet"></param>
		/// <param name="logger"></param>
		/// <returns></returns>
		protected abstract Task<PacketProcessResult> ProcessPacketAsync( GameSession session, TRequest packet, ILogger logger );

		/// <summary>
		/// 응답 패킷 생성 (하위 클래스 필수 구현)
		/// </summary>
		/// <param name="session"></param>
		/// <param name="packet"></param>
		/// <param name="result"></param>
		/// <param name="logger"></param>
		/// <returns></returns>
		protected abstract Task<TResponse> BuildResponseAsync( GameSession session, TRequest packet,
			PacketProcessResult result, ILogger logger );

		#endregion
	}
}
