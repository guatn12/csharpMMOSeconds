using System.Security.Cryptography;

namespace AuthServer.Services
{
	public class DummyPasswordHash
	{
		public string Value { get; }

		public DummyPasswordHash( IPasswordHashService passwordHashService )
		{
			string password = Convert.ToHexString( RandomNumberGenerator.GetBytes( 24 ) );
			Value = passwordHashService.HashPassword( password );
		}
	}
}
