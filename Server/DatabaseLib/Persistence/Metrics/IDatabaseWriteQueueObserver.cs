using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseLib.Persistence.Metrics
{
	public interface IDatabaseWriteQueueObserver
	{
		void SetQueueDepth( int depth );
		void RecordQueueFull();
		void RecordResult( DatabaseWriteStatus status );
		void ObserveProcessingDuration( double seconds );
	}
}
