using AuthContracts;
using AuthServer.Metrics;
using AuthServer.Models;
using AuthServer.Services;
using Grpc.Core;

namespace AuthServer.Grpc
{
	public class LoginTicketGrpcService : AuthInternal.AuthInternalBase
	{
		private readonly LoginTicketService _loginTicketService;
		private readonly ILogger<LoginTicketGrpcService> _logger;
		private readonly AuthMetrics _metrics;

		public LoginTicketGrpcService(
			LoginTicketService loginTicketService, ILogger<LoginTicketGrpcService> logger,
			AuthMetrics metrics )
		{
			_loginTicketService = loginTicketService;
			_logger = logger;
			_metrics = metrics;
		}

		public override async Task<RedeemLoginTicketReply> RedeemLoginTicket( RedeemLoginTicketRequest request, ServerCallContext context )
		{
			try
			{
				context.CancellationToken.ThrowIfCancellationRequested();

				LoginTicketRecord? record = await _loginTicketService.RedeemAsync( request.LoginTicket );
				if(record == null || record.AccountId <= 0)
				{
					_metrics.RecordRequest( "redeem_ticket", "invalid" );
					return new RedeemLoginTicketReply { Active = false };
				}

				_metrics.RecordRequest( "redeem_ticket", "success" );
				return new RedeemLoginTicketReply { Active = true, AccountId = record.AccountId };
			}
			catch(OperationCanceledException) when(context.CancellationToken.IsCancellationRequested)
			{
				_metrics.RecordRequest( "redeem_ticket", "cancelled" );
				throw;
			}
			catch(Exception ex)
			{
				_metrics.RecordRequest( "redeem_ticket", "dependency_error" );
				_logger.LogError( "로그인 티켓 교환 dependency 오류. ErrorType={ErrorType}", ex.GetType().Name );

				throw new RpcException( new Status( StatusCode.Unavailable,
					"Authentication service is temporarily unavailable." ) );
			}
		}
	}
}
