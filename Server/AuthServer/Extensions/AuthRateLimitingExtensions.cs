using AuthServer.DTOs;
using AuthServer.Metrics;
using System.Globalization;
using System.Threading.RateLimiting;

namespace AuthServer.Extensions
{
	public static class AuthRateLimitingExtensions
	{
		public static IServiceCollection AddAuthRateLimiting( this IServiceCollection services )
		{
			// 초과 응답과 제한 정책 설정
			services.AddRateLimiter( Options =>
			{
				Options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
				// OnRejected - 제한을 초과했을 때 실행되는 콜백
				Options.OnRejected = async ( context, cancellationToken ) =>
				{
					// 거절을 위한 새로운 httpcontext 객체 생성
					HttpContext http = context.HttpContext;
					// DI에서 매트릭 서비스를 가져와 결과 기록.
					http.RequestServices.GetRequiredService<AuthMetrics>().RecordHttpResult( http, "rate_limited" );

					// Lease - 제한기가 내린 허용 여부와 관련된 정보가 담긴 결과 객체. - 거절된 결과
					// MetadataName.RetryAfter는 다음 시도하기까지 기다릴 시간 TryGetMetadata를 통해 가져온다 - 있으면 true / 없으면 false
					if(context.Lease.TryGetMetadata( MetadataName.RetryAfter, out TimeSpan retryAfter ))
						http.Response.Headers.RetryAfter = Math.Max( 1, (int)Math.Ceiling( retryAfter.TotalSeconds ) )
							.ToString( CultureInfo.InvariantCulture );

					// 응답 json 생성.
					await http.Response.WriteAsJsonAsync(
						new AuthErrorResponse( "RateLimited", "Too many authentication requests." ),
						cancellationToken );
				};

				// 이름 있는 제한 규칙 등록
				Options.AddPolicy( "auth-register",
					// GetFixedWindowLimiter - 일정한 시간 구간마다 횟수를 제한
					context => RateLimitPartition.GetFixedWindowLimiter(
						context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
						_ => new FixedWindowRateLimiterOptions
						{
							PermitLimit = 5,						// 한 시간 구간에 5개 까지 허용
							Window = TimeSpan.FromMinutes( 1 ),		// 횟수를 계산하는 구간 길이
							QueueLimit = 0,							// 초과 요청을 대기열에 넣지 않고 거절
							AutoReplenishment = true				// 다음 구간이 되면 허용량 자동 보충
						} ) );

				Options.AddPolicy( "auth-login",
					context => RateLimitPartition.GetFixedWindowLimiter(
						context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
						_ => new FixedWindowRateLimiterOptions
						{
							PermitLimit = 10,
							Window = TimeSpan.FromMinutes( 1 ),
							QueueLimit = 0,
							AutoReplenishment = true
						} ) );
			} );
			
			return services;
		}
	}
}
