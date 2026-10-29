using Prometheus;

namespace AuthServer.Metrics
{
	public class AuthMetrics
	{
		private static readonly object HttpResultRecorded = new();

		private readonly Counter _requests = Prometheus.Metrics.CreateCounter( "mmo_auth_request_total",
			"Auth request results", new CounterConfiguration { LabelNames = new[] { "operation", "outcome" } } );
		
		private readonly Counter _ticketsIssued = Prometheus.Metrics.CreateCounter( "mmo_auth_login_tickets_issued_total",
			"Successfully issued one-time login tickets." );

		public void RecordRequest( string operation, string outcome )
		{
			_requests.WithLabels( operation, outcome ).Inc();
		}

		public void RecordTicketIssued()
		{
			_ticketsIssued.Inc();
		}

		public static string? GetOperation( HttpContext context )
		{
			if(HttpMethods.IsPost( context.Request.Method ) == false)
				return null;

			return context.Request.Path.Value?.TrimEnd( '/' ).ToLowerInvariant() switch
			{
				"/api/auth/register" => "register",
				"/api/auth/login" => "login",
				_ => null
			};
		}

		public void RecordHttpResult( HttpContext context, string outcome )
		{
			string? operation = GetOperation( context );
			if(operation == null || context.Items.ContainsKey( HttpResultRecorded ))
				return;

			context.Items[ HttpResultRecorded ] = true;
			RecordRequest( operation, outcome );
		}
	}
	
	
	
}
