using AuthServer.DTOs;
using AuthServer.Models;
using DatabaseLib;
using DatabaseLib.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthServer.Services
{
	public partial class AccountAuthService
	{
		private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
		private readonly LoginIdNormalizer _loginIdNormalizer;
		private readonly IPasswordHashService _passwordHashService;
		private readonly LoginTicketService _loginTicketService;
		private readonly DummyPasswordHash _dummyPasswordHash;
		private readonly ILogger<AccountAuthService> _logger;

		public AccountAuthService(
			IDbContextFactory<AppDbContext> dbContextFactory,
			LoginIdNormalizer loginIdNormalizer,
			IPasswordHashService passwordHashService,
			LoginTicketService loginTicketService,
			DummyPasswordHash dummyPasswordHash,
			ILogger<AccountAuthService> logger )
		{
			_dbContextFactory = dbContextFactory;
			_loginIdNormalizer = loginIdNormalizer;
			_passwordHashService = passwordHashService;
			_loginTicketService = loginTicketService;
			_dummyPasswordHash = dummyPasswordHash;
			_logger = logger;
		}

		public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string lookupKey;
			string passwordHash;

			try
			{
				if(string.IsNullOrWhiteSpace( request.LoginId ) || request.LoginId.Length < 3 || 50 < request.LoginId.Length ||
					string.IsNullOrEmpty( request.Password ))
					return RegisterResult.InvalidInput();
				lookupKey = _loginIdNormalizer.CreateLookupKey( request.LoginId );
				passwordHash = _passwordHashService.HashPassword( request.Password );
			}
			catch (ArgumentException) { return RegisterResult.InvalidInput(); }

			DateTime now = DateTime.UtcNow;
			var account = new AccountEntity
			{
				LoginId = request.LoginId,
				NormalizedLoginId = lookupKey,
				PasswordHash = passwordHash,
				CreatedAt = now,
				UpdatedAt = now
			};
			
			try
			{
				await using AppDbContext context = await _dbContextFactory.CreateDbContextAsync( cancellationToken );
				context.Accounts.Add( account );
				await context.SaveChangesAsync( cancellationToken );
				return RegisterResult.Success( account.AccountId );
			}
			catch(DbUpdateException ex) when(IsLoginIdUniqueViolation(ex))
			{
				return RegisterResult.LoginIdTaken();
			}
			catch(Exception ex) when (IsDatabaseFailure(ex))
			{
				throw new AuthDependencyException( "database", ex );
			}
		}

		public async Task<LoginResult> LoginAsync( LoginRequest request, CancellationToken cancellationToken )
		{
			cancellationToken.ThrowIfCancellationRequested();
			string lookupKey;
			try
			{
				if(string.IsNullOrWhiteSpace( request.LoginId ) || string.IsNullOrEmpty( request.Password ))
					return LoginResult.InvalidInput();
				lookupKey = _loginIdNormalizer.CreateLookupKey( request.LoginId );
			}
			catch(ArgumentException)
			{
				return LoginResult.InvalidInput();
			}

			try
			{
				await using AppDbContext context = await _dbContextFactory.CreateDbContextAsync( cancellationToken );
				AccountEntity? account = await context.Accounts.SingleOrDefaultAsync( x =>
					x.NormalizedLoginId == lookupKey, cancellationToken );
				bool verified;

				try
				{
					verified = _passwordHashService.VerifyPassword( request.Password,
						account?.PasswordHash ?? _dummyPasswordHash.Value );
				}
				catch(Exception ex)
				{
					throw new AuthDependencyException( "password_store", ex );
				}

				if(account == null || verified == false)
					return LoginResult.InvalidCredentials();
				
				cancellationToken.ThrowIfCancellationRequested();
				IssuedLoginTicket? ticket = await _loginTicketService.IssueAsync( account.AccountId );
				if(ticket == null)
					return LoginResult.DependencyUnavailable();

				try
				{
					cancellationToken.ThrowIfCancellationRequested();
					DateTime now = DateTime.UtcNow;
					account.LastLoginAt = now;
					account.UpdatedAt = now;
					if(_passwordHashService.NeedsRehash( account.PasswordHash ))
						account.PasswordHash = _passwordHashService.HashPassword( request.Password );
					await context.SaveChangesAsync( cancellationToken );

					return LoginResult.Success( ticket.Value, account.AccountId, ticket.ExpiresInSeconds );
				}
				catch
				{
					await RevokeAfterFailureAsync( ticket.Value );
					throw;
				}
			}
			catch(Exception ex) when(IsDatabaseFailure(ex))
			{
				throw new AuthDependencyException( "database", ex );
			}
		}

		private static bool IsLoginIdUniqueViolation( DbUpdateException ex ) =>
			ex.InnerException is PostgresException
			{
				SqlState: PostgresErrorCodes.UniqueViolation,
				ConstraintName: "ux_accounts_normalized_login_id"
			};

		private static bool IsDatabaseFailure( Exception ex ) =>
			ex is NpgsqlException or DbUpdateException or TimeoutException;

		private async Task RevokeAfterFailureAsync( string ticket )
		{
			try
			{
				// false는 이미 만료/삭제되어 키가 없다는 뜻이므로 보상 목표를 만족한다.
				await _loginTicketService.RevokeAsync( ticket );
			}
			catch(Exception ex)
			{
				// 원본 예외 메세지에는 내부 키 등이 있을 수 있어 종류만 기록한다.
				_logger.LogCritical( "로그인 티켓 보상 삭제 실패. Dependency={Dependency}, ErrorType={ErrorType}",
					"redis", ex.GetType().Name );
			}
		}
	}
}
