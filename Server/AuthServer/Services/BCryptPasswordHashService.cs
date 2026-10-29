using System.Text;

namespace AuthServer.Services
{
	public class BCryptPasswordHashService : IPasswordHashService
	{
		private const int WorkFactor = 12;
		private const int MinimumPasswordCharacters = 12;
		private const int MaximumPasswordBytes = 72;
		
		/// <summary>
		/// 새 비밀번호의 길이를 검사한 뒤, BCrypt로 저장용 해시를 생성.
		/// </summary>
		public string HashPassword( string password )
		{
			ValidateNewPassword( password );
			return BCrypt.Net.BCrypt.HashPassword( password, WorkFactor );
		}

		/// <summary>
		/// 입력 비밀번호를 저장된 BCrypt 해시와 비교. 빈 값이나, 72바이트 초과 입력은 false
		/// </summary>
		public bool VerifyPassword( string password, string passwordHash )
		{
			if(string.IsNullOrEmpty( password ) || string.IsNullOrEmpty( passwordHash ) ||
			   MaximumPasswordBytes < Encoding.UTF8.GetByteCount( password ))
			{
				return false;
			}

			return BCrypt.Net.BCrypt.Verify( password, passwordHash );
		}

		/// <summary>
		/// 저장된 해시의 비용 설정이 현재 WorkFactor보다 낮아 재해시가 필요한지 확인.
		/// </summary>
		public bool NeedsRehash( string passwordHash )
		{
			return BCrypt.Net.BCrypt.PasswordNeedsRehash( passwordHash, WorkFactor );
		}

		/// <summary>
		/// 새 비밀번호의 최소 길이와 UTF-8 기준 72바이트 한도를 검사.
		/// </summary>
		private static void ValidateNewPassword( string password )
		{
			ArgumentNullException.ThrowIfNull( password );

			if(password.Length < MinimumPasswordCharacters)
				throw new ArgumentException( "Password must contain at least 12 characters.", nameof(password) );

			if(MaximumPasswordBytes < Encoding.UTF8.GetByteCount( password ))
				throw new ArgumentException( "Password must not exceed 72 UTF-8 bytes.", nameof(password) );
		}
	}
}
