using AuthServer.DTOs;
using AuthServer.Metrics;
using AuthServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AuthServer.Controllers
{
	[ApiController]
	[Route("api/auth")]
	public class AuthController : ControllerBase
	{
		private readonly AccountAuthService _accountAuthService;
		private readonly AuthMetrics _metrics;
		private readonly IHostApplicationLifetime _lifetime;
		private readonly ILogger<AuthController> _logger;

		public AuthController( AccountAuthService accountAuthService, AuthMetrics metrics,
			IHostApplicationLifetime lifetime, ILogger<AuthController> logger )
		{
			_accountAuthService = accountAuthService;
			_metrics = metrics;
			_lifetime = lifetime;
			_logger = logger;
		}
		
		[HttpPost("register")]
		[EnableRateLimiting("auth-register")]
		public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
		{
			using var linked =
				CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, _lifetime.ApplicationStopping );
			RegisterResult result = await _accountAuthService.RegisterAsync( request, linked.Token );
			return result.Status switch
			{
				RegisterStatus.Success => Reply( 201, new RegisterResponse( result.AccountId ), "success" ),
				RegisterStatus.InvalidInput => Reply( 400, new AuthErrorResponse( "InvalidInput",
					"Registration input is invalid." ), "invalid" ),
				RegisterStatus.LoginIdTaken => Reply( 409, new AuthErrorResponse( "LoginIdTaken",
					"Login ID is already taken." ), "duplicate" ),
				_ => throw new InvalidOperationException( "Unknown registration result." )
			};
		}

		[ HttpPost( "login" ) ]
		[ EnableRateLimiting( "auth-login" ) ]
		public async Task<IActionResult> Login( [ FromBody ] LoginRequest request, CancellationToken cancellationToken )
		{
			using var linked =
				CancellationTokenSource.CreateLinkedTokenSource( cancellationToken, _lifetime.ApplicationStopping );
			LoginResult result = await _accountAuthService.LoginAsync( request, linked.Token );
			if(result.Status == LoginStatus.DependencyUnavailable)
				_logger.LogWarning( "인증 요청 실패. Operation={Operation}, Dependency={Dependency}", "login", "redis" );
			return result.Status switch
			{
				LoginStatus.Success => Reply( 200, new LoginResponse( result.LoginTicket, result.AccountId,
					result.ExpiresInSeconds ), "success" ),
				LoginStatus.InvalidInput => Reply( 400, new AuthErrorResponse( "InvalidInput",
					"Login input is invalid." ), "invalid" ),
				LoginStatus.InvalidCredientials => Reply( 401, new AuthErrorResponse( "InvalidCredentials",
					"Login ID or password is invalid." ), "invalid" ),
				LoginStatus.DependencyUnavailable => Reply( 503, new AuthErrorResponse( "DependencyUnavailable",
					"Authentication is temporarily unavailable" ), "dependency_error" ),
				_ => throw new InvalidOperationException( "Unknown login result." )
			};
		}

		private IActionResult Reply( int statusCode, object response, string outcome )
		{
			_metrics.RecordHttpResult( HttpContext, outcome );
			return StatusCode( statusCode, response );
		}
	}
}
