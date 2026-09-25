using AuthServer.Options;
using Microsoft.Extensions.Options;
using System.Text;

namespace AuthServer.Services
{
	public class LoginIdNormalizer
	{
		private const int MaxLoginIdLength = 50;
		private readonly AuthSettings _settings;

		public LoginIdNormalizer(IOptions<AuthSettings> settings)
		{
			_settings = settings.Value;
		}

		public string CreateLookupKey(string loginId)
		{
			ArgumentNullException.ThrowIfNull( loginId );

			string trimmed = loginId.Trim();
			if(string.Equals(loginId, trimmed, StringComparison.Ordinal) == false)
				throw new ArgumentException( "Login ID cannot contain leading or trailing whitespace.", nameof( loginId ) );

			string lookupKey = _settings.LoginIdNormalizationEnabled ? loginId.Normalize(NormalizationForm.FormKC).ToUpperInvariant() : loginId;
			if(lookupKey.Length == 0 || MaxLoginIdLength < lookupKey.Length)
				throw new ArgumentException( "Login ID length is invalid.", nameof( loginId ) );

			return lookupKey;
		}
	}
}
