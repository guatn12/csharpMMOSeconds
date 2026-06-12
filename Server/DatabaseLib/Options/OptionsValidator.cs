using Microsoft.Extensions.Options;

namespace DatabaseLib.Options
{
	public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
	{
		public ValidateOptionsResult Validate(string name, DatabaseOptions options)
		{
			var failList = new List<string>();
			if(string.IsNullOrWhiteSpace( options.ConnectionString )) failList.Add( "Database ConnectionString is required" );

			if(options.MaxPoolSize < options.MinPoolSize) failList.Add( $"MaxPoolSize({options.MaxPoolSize}) must be  >= MinPoolSize({options.MinPoolSize})" );

			if(options.CommandTimeout <= 0) failList.Add( "CommandTimeout must be > 0" );

			return failList.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail( failList );
		}
	}

	public sealed class RedisOptionsValidator : IValidateOptions<RedisOptions>
	{
		public ValidateOptionsResult Validate( string name, RedisOptions options )
		{
			var failList = new List<string>();
			if(string.IsNullOrWhiteSpace( options.ConnectionString )) failList.Add( "Redis ConnectionString is required" );

			return failList.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail( failList );
		}
	}
}
