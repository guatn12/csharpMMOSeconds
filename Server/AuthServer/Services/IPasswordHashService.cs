namespace AuthServer.Services
{
	public interface IPasswordHashService
	{
		string HashPassword( string password );
		bool VerifyPassword( string password, string passwordHash );
		bool NeedsRehash( string passwordHash );
	}
}
