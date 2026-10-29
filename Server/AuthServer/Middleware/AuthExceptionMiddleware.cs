using AuthServer.DTOs;
using AuthServer.Metrics;
using AuthServer.Services;

namespace AuthServer.Middleware
{
	public class AuthExceptionMiddleware
	{
		private readonly RequestDelegate _next;
		private readonly ILogger<AuthExceptionMiddleware> _logger;

		public AuthExceptionMiddleware( RequestDelegate next, ILogger<AuthExceptionMiddleware> logger )
		{
			_next = next;
			_logger = logger;
		}

		public async Task InvokeAsync( HttpContext context, AuthMetrics metrics, IHostApplicationLifetime lifetime )
		{
			if(context.Request.Path.StartsWithSegments( "/api/auth" ) == false)
			{
				await _next( context );
				return;
			}

			try
			{
				await _next( context );
			}
			catch(OperationCanceledException) when(context.RequestAborted.IsCancellationRequested ||
			                                       lifetime.ApplicationStopping.IsCancellationRequested)
			{
				metrics.RecordHttpResult( context, "cancelled" );
				throw;
			}
			catch(Exception ex)
			{
				if(context.Response.HasStarted)
					throw;

				bool dependency = ex is AuthDependencyException;
				bool badRequest = ex is BadHttpRequestException;
				int status = dependency ? 503 : badRequest ? 400 : 500;
				metrics.RecordHttpResult( context,
					dependency ? "dependency_error" : badRequest ? "invalid" : "unexpected_error" );

				_logger.LogError(
					"인증 요청 예외. Operation={Operation}, Status={Status}, RequestId={RequestId}, ErrorType={ErrorType}",
					AuthMetrics.GetOperation( context ), status, context.TraceIdentifier, ex.GetType().Name );
				
				context.Response.StatusCode = status;
				if(dependency || badRequest)
				{
					await context.Response.WriteAsJsonAsync( new AuthErrorResponse(
							dependency ? "DependencyUnavailable" : "InvalidInput",
							dependency ? "Authentication is temporarily unavailable." : "Request input is invalid." ),
						context.RequestAborted );
					return;
				}

				await Results.Problem( statusCode: 500, title: "Unexpected authentication error.",
						extensions: new Dictionary<string, object?> { [ "requestId" ] = context.TraceIdentifier } )
					.ExecuteAsync( context );
			}
		}
	}
}
