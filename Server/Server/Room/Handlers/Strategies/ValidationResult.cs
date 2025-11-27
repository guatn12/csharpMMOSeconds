using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Room.Handlers.Strategies
{
	/// <summary>
	/// 패킷 검증 결과
	/// </summary>
	public class ValidationResult
	{
		// 검증 성공 여부
		public bool IsValid { get; set; }

		// 실패 시 에러 메세지(로그용)
		public string ErrorMessage { get; set; }

		// 성공 결과 생성 (Factory Method)
		public static ValidationResult Success() => new()
		{
			IsValid = true,
		};

		// 실패 결과 생성 (Factory Method)
		public static ValidationResult Failure( string message ) => new()
		{
			ErrorMessage = message,
			IsValid = false
		};
	}

	/// <summary>
	/// 패킷 처리 결과 (비즈니스 로직 실행 결과)
	/// </summary>
	public class PacketProcessResult
	{
		// 처리 성공 여부
		public bool Success { get; set; }

		// 응답 생성에 필요한 데이터 - 다양한 타입의 데이터 저장을 위해 object 사용
		public object Data { get; set; }

		// 실패 시 메세지
		public string Message { get; set; }

		// 성공 결과 생성 (Factory Method)
		public static PacketProcessResult Ok( object data = null ) => new()
		{
			Data = data,
			Success = true
		};

		// 실패 결과 생성 (Factory Method)
		public static PacketProcessResult Fail( string message ) => new()
		{
			Success = false,
			Message = message
		};
	}
}
