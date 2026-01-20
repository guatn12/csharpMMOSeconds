using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public interface IJobQueueManager
	{
		JobPool JobPool { get; }
		void Start( int workerCount, int channelCapacity = 1000 );
		Task StopAsync();

		ValueTask RegisterAsync( IJobOwner jobOwner );
	}
}
