namespace AuthServer.Options
{
	public class AuthSettings
	{
		public bool LoginIdNormalizationEnabled { get; init; } = true;
		public int LoginTicketTtlSeconds { get; init; } = 120;
	}
}
