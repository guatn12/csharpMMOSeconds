using DatabaseLib.Persistence;
using DatabaseLib.Persistence.Metrics;

namespace Server.Infra.Metrics
{
	public class PrometheusDatabaseWriteQueueObserver : IDatabaseWriteQueueObserver
	{
		public void ObserveProcessingDuration( double seconds )
		{
			GameMetrics.DatabaseWriteCommandDuration.Observe( seconds );
		}

		public void RecordQueueFull()
		{
			GameMetrics.DatabaseWriteQueueFull.Inc();
		}

		public void RecordResult( DatabaseWriteStatus status )
		{
			GameMetrics.DatabaseWriteResults.WithLabels( status.ToString() ).Inc();
		}

		public void SetQueueDepth( int depth )
		{
			GameMetrics.DatabaseWriteQueueDepth.Set( depth );
		}
	}
}
