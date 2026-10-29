using AuthContracts;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Config;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Services.Auth
{
	public class GrpcAuthGateway : IAuthGateway
	{
		private readonly AuthInternal.AuthInternalClient _client;
		private readonly AuthGrpcConfig _settings;
		private readonly ILogger<GrpcAuthGateway> _logger;

		public GrpcAuthGateway( AuthInternal.AuthInternalClient client,
			IOptions<ServerSettings> settings, ILogger<GrpcAuthGateway> logger )
		{
			_client = client;
			_settings = settings.Value.AuthGrpc;
			_logger = logger;
		}

		public async Task<AuthRedeemResult> RedeemLoginTicketAsync( string loginTicket,
			CancellationToken cancellationToken )
		{
			var request = new RedeemLoginTicketRequest { LoginTicket = loginTicket };

			DateTime deadline = DateTime.UtcNow.AddMilliseconds( _settings.DeadLineMs );

			try
			{
				RedeemLoginTicketReply reply =
					await _client.RedeemLoginTicketAsync( request, deadline: deadline,
						cancellationToken: cancellationToken );
				if(reply.Active == false || reply.AccountId <= 0)
				{
					return new AuthRedeemResult( AuthRedeemStatus.Invalid, 0 );
				}

				return new AuthRedeemResult( AuthRedeemStatus.Active, reply.AccountId );
			}
			catch(RpcException ex) when(ex.StatusCode == StatusCode.DeadlineExceeded)
			{
				return new AuthRedeemResult( AuthRedeemStatus.DeadlineExceeded, 0 );
			}
			catch(RpcException ex) when(ex.StatusCode == StatusCode.Unavailable)
			{
				return new AuthRedeemResult( AuthRedeemStatus.DependencyUnavailable, 0 );
			}
			catch(RpcException ex) when(ex.StatusCode == StatusCode.Cancelled &&
			                            cancellationToken.IsCancellationRequested)
			{
				throw new OperationCanceledException( "Auth gRPC call was cancelled.", ex, cancellationToken );
			}
			catch(RpcException ex)
			{
				_logger.LogError( ex, "Auth gRPC 호출 실패. StatusCode={StatudeCode}", ex.StatusCode );

				return new AuthRedeemResult( AuthRedeemStatus.DependencyUnavailable, 0 );
			}
		}
	}
}
