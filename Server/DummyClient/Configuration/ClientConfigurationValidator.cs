using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DummyClient.Configuration
{
	internal class ClientConfigurationValidator : IValidateOptions<ClientConfiguration>
	{
		public ValidateOptionsResult Validate(string name, ClientConfiguration  options)
		{
			List<string> failures = new List<string>();

			if(string.IsNullOrWhiteSpace( options.Connection.ServerHost ))
				failures.Add( "Connection.ServerHost는 필수입니다." );

			if(options.Connection.ServerPort <= 0 || 65535 < options.Connection.ServerPort)
				failures.Add( "Connection.ServerPort는 1-65535 범위여야 합니다." );

			if(options.Simulation.ClientCount <= 0)
				failures.Add( "Simulation.ClientCount는 1이상이어야 합니다." );

			return 0 < failures.Count
				? ValidateOptionsResult.Fail(failures)
				: ValidateOptionsResult.Success;
		}
	}
}
