using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.TestHelpers
{
	public class TestJobSerializerWithGate : JobSerializer
	{
		public bool AcceptJobs { get; set; } = true;

		public TestJobSerializerWithGate( IJobQueueManager jobQueueManager )
			: base( jobQueueManager )
		{
		}

		protected override bool CanAcceptJob() => AcceptJobs;

		public async ValueTask ProcessJobsForTest() => await ((IJobOwner)this).ProcessJobsAsync();
	}
}
