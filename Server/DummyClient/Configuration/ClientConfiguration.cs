using System.ComponentModel.DataAnnotations;

namespace DummyClient.Configuration
{
	public class ClientConfiguration
	{
		public ConnectionSettings Connection { get; set; } = new();
		public SimulationSettings Simulation { get; set; } = new();
		public LoggingSettings Logging { get; set; } = new();
	}

	public class ConnectionSettings
	{
		[Required]
		public string ServerHost { get; set; } = "127.0.0.1";

		[Range( 1, 65535 )]
		public int ServerPort { get; set; } = 7777;

		[Range( 1000, 60000 )]
		public int ConnectionTimeoutMs { get; set; } = 5000;

		[Range( 1000, 30000 )]
		public int ReconnectIntervalMs { get; set; } = 3000;

		[Range( 1, 10 )]
		public int MaxReconnectAttempts { get; set; } = 5;
	}

	public class SimulationSettings
	{
		[Range( 1, 100 )]
		public int ClientCount { get; set; } = 1;

		[Range( 100, 10000 )]
		public int MessageIntervalMs { get; set; } = 1000;

		public bool AutoReconnect { get; set; } = true;
		public bool EnableHeartbeat { get; set; } = true;

		[Range( 10000, 120000 )]
		public int HeartbeatIntervalMs { get; set; } = 30000;
	}

	public class LoggingSettings
	{
		[Required]
		public string Level { get; set; } = "Information";

		public bool EnableConsoleLogging { get; set; } = true;
		public bool EnableFileLogging { get; set; } = false;
		public string OutputPath { get; set; } = "logs/log-.log";
	}
}
