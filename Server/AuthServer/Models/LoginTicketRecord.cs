namespace AuthServer.Models
{
	public sealed record LoginTicketRecord( long AccountId, DateTime IssuedAtUtc );

	public sealed record IssuedLoginTicket( string Value, int ExpiresInSeconds );
}
