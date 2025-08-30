using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Config;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Infra
{
	public class RedisService : IDisposable
	{
		private readonly ILogger<RedisService> _logger;
		private readonly string _connectionString;
		private ConnectionMultiplexer _connection;
		private readonly object _lock  = new object();

		public RedisService(IOptions<ServerSettings> serverSettings, ILogger<RedisService> logger)
		{
			_logger = logger;
			_connectionString = serverSettings.Value.Redis.ConnectionString;
		}

		public async Task ConnectAsync()
		{
			if(_connection != null && _connection.IsConnected)
			{
				return;
			}

			lock(_lock)
			{
				if(_connection != null && _connection.IsConnected)
				{
					return;
				}

				try
				{
					_logger.LogInformation( "Connecting to Redis..." );
					_connection = ConnectionMultiplexer.Connect( _connectionString );
					_logger.LogInformation( "Successfully connected to Redis." );
				}
				catch (Exception ex)
				{
					_logger.LogError( ex, "Failed to connect to Redis" );
					throw;
				}
			}
		}

		public IDatabase GetDatabase(int db = -1)
		{
			if(_connection == null || !_connection.IsConnected)
			{
				throw new InvalidOperationException( "Redis is not connected." );
			}
			return _connection.GetDatabase( db );
		}

		public void Dispose()
		{
			_connection?.Dispose();
		}
	}
}
