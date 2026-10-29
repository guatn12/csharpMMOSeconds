using Microsoft.Extensions.Configuration;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DummyClient.Services
{
	public class AuthApiClient : IDisposable
	{
		private readonly HttpClient _http;
		private readonly string _prefix;
		private readonly string _password;
		private readonly int _maxAttempts;

		public AuthApiClient( IConfiguration configuration )
		{
			string address = configuration[ "AuthClient:BaseUrl" ] ?? "https://localhost:7001";
			if(!Uri.TryCreate( address, UriKind.Absolute, out Uri uri ) || uri.Scheme != "https")
				throw new InvalidOperationException( "AuthClient BaseUrl must be HTTPS" );

			_prefix = configuration[ "AuthClient:LoginIdPrefix" ] ?? "dummy";
			_password = configuration[ "AuthClient:Password" ] ?? "";
			_maxAttempts = configuration.GetValue<int>( "AuthClient:MaxAttempts", 5 );
			if(string.IsNullOrWhiteSpace( _prefix ) || string.IsNullOrEmpty( _password ) || _maxAttempts < 1 ||
			   10 < _maxAttempts)
				throw new InvalidOperationException( "AuthClient settings are invalid or password is missing." );

			_http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds( 10 ) };
		}

		public async Task<string> AcquireTicketAsync( int clientId, CancellationToken token )
		{
			using(HttpResponseMessage health =
			      await SendAsync( () => new HttpRequestMessage( HttpMethod.Get, "/health" ), token ))
			{
				if(health.StatusCode != HttpStatusCode.OK)
					throw new InvalidOperationException( "Auth health check was rejected." );
			}

			string loginId = _prefix + "-" + clientId;
			using(HttpResponseMessage register =
			      await SendAsync( () => CreatePost( "/api/auth/register", loginId ), token ))
			{
				if(register.StatusCode != HttpStatusCode.Created && register.StatusCode != HttpStatusCode.Conflict)
					throw new InvalidOperationException( "Registration was rejected. Check account input." );
			}

			using HttpResponseMessage login = await SendAsync( () => CreatePost( "/api/auth/login", loginId ), token );
			if(login.StatusCode != HttpStatusCode.OK)
				throw new InvalidOperationException( "Login was rejected. Check account input." );

			LoginReply reply = await login.Content.ReadFromJsonAsync<LoginReply>( cancellationToken: token );
			if(reply == null || string.IsNullOrEmpty( reply.LoginTicket ) || reply.AccountId <= 0 ||
			   reply.ExpiresInSeconds <= 0)
				throw new InvalidOperationException( "Auth login response is invalid." );

			return reply.LoginTicket;
		}

		private HttpRequestMessage CreatePost( string path, string loginId ) =>
			new HttpRequestMessage( HttpMethod.Post, path )
			{
				Content = JsonContent.Create( new { LoginId = loginId, Password = _password } )
			};

		private async Task<HttpResponseMessage> SendAsync( Func<HttpRequestMessage> createRequest,
			CancellationToken token )
		{
			for(int attempt = 1; attempt <= _maxAttempts; attempt++)
			{
				token.ThrowIfCancellationRequested();
				TimeSpan delay = TimeSpan.FromSeconds( 1 );
				try
				{
					using HttpRequestMessage request = createRequest();
					HttpResponseMessage response = await _http.SendAsync( request, token );
					int status = (int)response.StatusCode;
					if(status != 429 && status != 502 && status != 503 && status != 504)
						return response;

					TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
					if(retryAfter == null && response.Headers.RetryAfter?.Date is DateTimeOffset date)
						retryAfter = date - DateTimeOffset.UtcNow;

					if(retryAfter.HasValue && delay < retryAfter.Value)
						delay = retryAfter.Value;
					
					response.Dispose();
				}
				catch(HttpRequestException) { }
				catch(OperationCanceledException) when(!token.IsCancellationRequested) { }

				if(attempt == _maxAttempts)
					break;
				
				// 개발 클라이언트가 무제한 대기를 받아들이지 않는다.
				if(TimeSpan.FromMinutes( 2 ) < delay)
					throw new InvalidOperationException( "Auth retry delay exceeds the client limits." );

				await Task.Delay( delay, token );
			}

			throw new InvalidOperationException( "Auth service did not become available within the retry limits." );
		}

		private sealed class LoginReply
		{
			public LoginReply() { }
			public string LoginTicket { get; set; }
			public long AccountId { get; set; }
			public int ExpiresInSeconds { get; set; }
		}

		public void Dispose() => _http.Dispose();
	}
}
