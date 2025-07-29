using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DummyClient.Configuration.Services
{
	public class ClientConfigurationService : IClientConfigurationService
	{
		private readonly IOptionsMonitor<ClientConfiguration> _options;
		private readonly ILogger<ClientConfigurationService> _logger;

		public ClientConfigurationService(
			IOptionsMonitor<ClientConfiguration> options,
			ILogger<ClientConfigurationService> logger )
		{
			_options = options;
			_logger = logger;
		}

		public ClientConfiguration Current => _options.CurrentValue;

		public void RegisterChangeCallBack( Action<ClientConfiguration> callback )
		{
			_options.OnChange( callback );
		}
	}
}
