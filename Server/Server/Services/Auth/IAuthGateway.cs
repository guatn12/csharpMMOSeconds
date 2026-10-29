using System.Threading;
using System.Threading.Tasks;

namespace Server.Services.Auth
{
	public enum AuthRedeemStatus
	{
		Active,						// 티켓 교환에 성공
		Invalid,					// 무효, 만료, 이미 사용된 티켓 등으로 인증 거부
		DependencyUnavailable,		// 인증 서비스 통신 또는 처리 오류로 교환 실패
		DeadlineExceeded			// gRPC 호출 제한 시간 초과로 결과 확인 실패
	}

	public readonly record struct AuthRedeemResult( AuthRedeemStatus Status, long AccountId );
	
	public interface IAuthGateway
	{
		Task<AuthRedeemResult> RedeemLoginTicketAsync( string loginTicket, CancellationToken cancellationToken );
	}
}
