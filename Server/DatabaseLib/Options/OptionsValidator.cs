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

			if(options.WriteQueueCapacity < 32 || 8192 < options.WriteQueueCapacity)
				failList.Add( "WriteQueueCapacity must be between 32 and 8192" );

			if(options.WriteQueueCriticalEnqueueTimeoutMs < 1000 || 30000 < options.WriteQueueCriticalEnqueueTimeoutMs)
				failList.Add( "WriteQueueCriticalEnqueueTimeoutMs must be between 1000 and 30000" );

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
