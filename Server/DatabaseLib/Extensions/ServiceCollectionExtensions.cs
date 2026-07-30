using DatabaseLib.Options;
using DatabaseLib.Redis;
using DatabaseLib.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace DatabaseLib.Extensions
{
	public static class ServiceCollectionExtensions
	{
		public static IServiceCollection AddDatabaseLib( this IServiceCollection services, IConfiguration config )
		{
			// Options 바인딩 + 라이브러리 소유 검증 (실행 시 실패)
			services.AddOptions<DatabaseOptions>().Bind( config.GetSection( "ServerSettings:Database" ) ).ValidateOnStart();
			services.AddOptions<RedisOptions>().Bind( config.GetSection( "ServerSettings:Redis" ) ).ValidateOnStart();
			services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
			services.AddSingleton<IValidateOptions<RedisOptions>, RedisOptionsValidator>();

			var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

			services.AddDbContextFactory<AppDbContext>( ( sp, options ) =>
			{
				var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
				options.UseNpgsql( BuildConnectionString( db, environment ), npgsql =>
				{
					npgsql.CommandTimeout( db.CommandTimeout );
					npgsql.MigrationsAssembly( "DatabaseLib" );
				} );

				if(environment == "Development" && db.EnableSensitiveDataLogging)
				{
					options.EnableSensitiveDataLogging();
					options.EnableDetailedErrors();
				}
			} );

			services.AddSingleton<IConnectionMultiplexer>( sp =>
			{
				var redis = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
				var configurationOptions = new ConfigurationOptions
				{
					EndPoints = { redis.ConnectionString },
					AbortOnConnectFail = false, // 연결 실패 시 서버 중단 방지
					ConnectTimeout = 5000,
					ConnectRetry = 3
				};

				return ConnectionMultiplexer.Connect( configurationOptions );
			} );

			services.AddSingleton<IRedisService, RedisService>();
			services.AddSingleton<PlayerCacheService>();
			services.AddSingleton<InventoryCacheService>();
			services.AddSingleton<EquipmentCacheService>();

			return services;
		}

		private static string BuildConnectionString(DatabaseOptions options, string environment )
		{
			// Npgsql Connection Pool 최적화
			var connectionStringBuilder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
			{
				MinPoolSize = options.MinPoolSize,
				MaxPoolSize = options.MaxPoolSize,
				ConnectionLifetime = options.ConnectionTimeout,
				Timeout = options.ConnectionTimeout,
				CommandTimeout = options.CommandTimeout,

				// 추가 성능 최적화
				Pooling = true,
				IncludeErrorDetail = environment == "Development"
			};

			return connectionStringBuilder.ConnectionString;
		}
	}
}
