using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DummyClient.Configuration.Services
{
	public interface IClientConfigurationService
	{
		ClientConfiguration Current { get; }
		void RegisterChangeCallBack( Action<ClientConfiguration> callback );
	}
}
