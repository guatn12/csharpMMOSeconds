namespace AuthServer.Services
{
	public enum RegisterStatus { Success, InvalidInput, LoginIdTaken }
	public enum LoginStatus { Success, InvalidInput, InvalidCredientials, DependencyUnavailable }

	public record RegisterResult( RegisterStatus Status, long AccountId = 0 )
	{
		public static RegisterResult Success( long accountId ) => new( RegisterStatus.Success, accountId );
		public static RegisterResult InvalidInput() => new( RegisterStatus.InvalidInput );
		public static RegisterResult LoginIdTaken() => new( RegisterStatus.LoginIdTaken );
	}

	public record LoginResult( LoginStatus Status, string LoginTicket = "", long AccountId = 0, int ExpiresInSeconds = 0 )
	{
		public static LoginResult Success( string ticket, long id, int ttl ) =>
			new( LoginStatus.Success, ticket, id, ttl );
		public static LoginResult InvalidInput() => new( LoginStatus.InvalidInput );
		public static LoginResult InvalidCredentials() => new( LoginStatus.InvalidCredientials );
		public static LoginResult DependencyUnavailable() => new( LoginStatus.DependencyUnavailable );
	}

	public class AuthDependencyException : Exception
	{
		public string Dependency { get; }
		public AuthDependencyException(string dependency, Exception? inner = null)
			: base("Authentication dependency unavailable.", inner) => dependency = dependency;
	}
}
