using AuthServer.Metrics;
using AuthServer.Models;
using AuthServer.Options;
using DatabaseLib.Redis;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace AuthServer.Services
{
	/// <summary>
	/// GameServer 입장에 사용할 짧은 수명의 일회성 로그인 티켓 관리
	/// 티켓 발급(Issue), 조회와 동시에 소비(Redeem), 폐기(Revoke) 기능
	/// </summary>
	public class LoginTicketService
	{
		private const int TicketByteLength = 32;
		private const int EncodedTicketLength = 43;

		private readonly IRedisService _redisService;
		private readonly AuthSettings _authSettings;
		private readonly AuthMetrics _metrics;
		private readonly ILogger<LoginTicketService> _logger;
		
		public LoginTicketService( IRedisService redisService, IOptions<AuthSettings> authSettings,
			AuthMetrics metrics, ILogger<LoginTicketService> logger )
		{
			_redisService = redisService;
			_authSettings = authSettings.Value;
			_metrics = metrics;
			_logger = logger;
		}

		/// <summary>
		/// 계정에 대한 새 로그인 티켓을 생성, 연결된 계정 정보를 Redis에 저장
		/// </summary>
		public async Task<IssuedLoginTicket?> IssueAsync( long accountId )
		{
			byte[] randomBytes = RandomNumberGenerator.GetBytes( TicketByteLength );
			string ticket = WebEncoders.Base64UrlEncode( randomBytes );
			// 원본 티켓 대신, SHA-256 해시로 Redis 조회 키를 생성
			string redisKey = CreateRedisKey( ticket );
			TimeSpan ttl = TimeSpan.FromSeconds( _authSettings.LoginTicketTtlSeconds );

			var record = new LoginTicketRecord( accountId, DateTime.UtcNow );

			bool stored;
			try
			{
				stored = await _redisService.SetAsync( redisKey, record, ttl );
			}
			catch(Exception ex)
			{
				throw new AuthDependencyException( "redis", ex );
			}

			if(stored == false)
				return null;

			_metrics.RecordTicketIssued();
			return new IssuedLoginTicket( ticket, _authSettings.LoginTicketTtlSeconds );
		}

		/// <summary>
		/// 티켓에 연결된 계정 정보를 조회하면서 티켓을 동시에 삭제.
		/// 정상적인 로그인 티켓 교환에 사용.
		/// </summary>
		public Task<LoginTicketRecord?> RedeemAsync( string ticket )
		{
			if(string.IsNullOrEmpty( ticket ) || ticket.Length != EncodedTicketLength)
			{
				return Task.FromResult<LoginTicketRecord?>( null );
			}

			return _redisService.GetDeleteAsync<LoginTicketRecord>( CreateRedisKey( ticket ) );
		}

		/// <summary>
		/// 발급 후 DB 갱신 실패나, 요청 취소가 발생했을 때 티켓을 폐기한다.
		/// 클라이언트에게 아직 응답하지 않은 티켓의 보상 삭제에 사용한다.
		/// </summary>
		public Task<bool> RevokeAsync( string ticket )
		{
			return _redisService.DeleteAsync( CreateRedisKey( ticket ) );
		}

		/// <summary>
		/// 원본 티켓을 SHA-256 해시로 변환하여 일관된 Redis 논리 키를 만든다.
		/// 발급, 소비, 폐기에서 같은 티켓을 같은 키로 변환하는 공통 경로.
		/// </summary>
		private static string CreateRedisKey( string ticket )
		{
			byte[] ticketBytes = Encoding.UTF8.GetBytes( ticket );
			byte[] ticketHash = SHA256.HashData( ticketBytes );
			string ticketHashHex = Convert.ToHexString( ticketHash ).ToLowerInvariant();

			return RedisKeys.LoginTicket( ticketHashHex );
		}
	}
}
