using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Issues
{

	public class JobSerializerTestRoom : JobSerializer
	{
		public JobSerializerTestRoom( IJobQueueManager jobQueueManager ) : base( jobQueueManager )
		{
		}

		public async ValueTask ProcessJobsForTest() => await ((IJobOwner)this).ProcessJobsAsync();
	}


}
