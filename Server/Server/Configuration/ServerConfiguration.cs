using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace Server.Configuration
{
	public class ServerConfiguration
	{
		[Required]
		public NetworkSettings Network { get; set; } = new();

		[Required]
		public LoggingSettings Logging { get; set; } = new();

		public SecuritySettings Security { get; set; } = new();

		public DatabaseSettings Database { get; set; } = new();

		[Required]
		public JobQueueSettings JobQueue { get; set; } = new();

		[Required]
		public RoomSettings Room { get; set; } = new();
	}
}
