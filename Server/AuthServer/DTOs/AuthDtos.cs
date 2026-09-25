using System.ComponentModel.DataAnnotations;

namespace AuthServer.DTOs
{
	public class RegisterRequest
	{
		[Required]
		[StringLength( 50, MinimumLength = 3 )]
		public string LoginId { get; init; }

		[Required]
		[MinLength( 12 )]
		public string Password { get; init; }
	}

	public record RegisterResponse( long AccountId );

	public class  LoginRequest
	{
		[Required]
		public string LoginId { get; init; }

		[Required]
		public string Password { get; init; }
	}

	public record LoginResponse(string LoginTicket, long AccountId, int ExpiresInSeconds );

	public record AuthErrorResponse( string Code, string Message );
}
