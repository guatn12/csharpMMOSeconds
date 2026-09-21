using AuthServer.DTOs;
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

		private static bool IsLoginIdUniqueViolation( DbUpdateException ex ) =>
			ex.InnerException is PostgresException
			{
				SqlState: PostgresErrorCodes.UniqueViolation,
				ConstraintName: "ux_accounts_normalized_login_id"
			};

		private static bool IsDatabaseFailure( Exception ex ) =>
			ex is NpgsqlException or DbUpdateException or TimeoutException;
	}
}
