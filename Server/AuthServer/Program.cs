
using AuthServer.DTOs;
using AuthServer.Extensions;
using AuthServer.Grpc;
using AuthServer.HealthChecks;
using AuthServer.Metrics;
using AuthServer.Middleware;
using AuthServer.Options;
using AuthServer.Services;
using DatabaseLib;
using DatabaseLib.Extensions;
using Microsoft.AspNetCore.Mvc;
using Prometheus;
using Serilog;
using Serilog.Filters;

namespace AuthServer
{
	public class Program
	{
		public static void Main( string[] args )
		{
			var builder = WebApplication.CreateBuilder(args);

			builder.Host.UseSerilog((context, services, configuration) => configuration
				.ReadFrom.Configuration(context.Configuration)
				.Enrich.FromLogContext()
				// 인증 SQL과 provider 예외 상세는 모든 sink에서 제외한다.
				.Filter.ByExcluding(Matching.FromSource("Microsoft.EntityFrameworkCore")));

			// Add services to the container.
			builder.Services.AddDatabaseLib( builder.Configuration, builder.Environment );
			builder.Services.AddOptions<AuthSettings>()
				.Bind( builder.Configuration.GetSection( "AuthSettings" ) )
				.Validate( settings => 0 < settings.LoginTicketTtlSeconds, 
					"AuthSettings:LoginTicketTtlSeconds must be greater than 0." )
				.ValidateOnStart();

			builder.Services.AddSingleton<LoginIdNormalizer>();
			builder.Services.AddSingleton<IPasswordHashService, BCryptPasswordHashService>();
			builder.Services.AddSingleton<DummyPasswordHash>();
			builder.Services.AddSingleton<AuthMetrics>();
			builder.Services.AddSingleton<LoginTicketService>();
			builder.Services.AddScoped<AccountAuthService>();
			builder.Services.AddControllers().ConfigureApiBehaviorOptions( options =>
			{
				options.InvalidModelStateResponseFactory = context =>
				{
					context.HttpContext.RequestServices.GetRequiredService<AuthMetrics>()
						.RecordHttpResult( context.HttpContext, "invalid" );
					return new BadRequestObjectResult( new AuthErrorResponse( "InvalidInput",
						"Request input is invalid" ) );
				};
			} );
			
			// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
			builder.Services.AddGrpc();
			builder.Services.AddProblemDetails();
			builder.Services.AddAuthRateLimiting();
			builder.Services.AddHealthChecks()
				.AddDbContextCheck<AppDbContext>( "database" )
				.AddCheck<AuthRedisHealthChecks>( "redis" );
			
			builder.Services.AddEndpointsApiExplorer();
			builder.Services.AddSwaggerGen();

			var app = builder.Build();
			// singletone의 지연 생성을 시작 시 초기화로 바꾼다.
			_ = app.Services.GetRequiredService<DummyPasswordHash>();
			
			// Configure the HTTP request pipeline.
			if(app.Environment.IsDevelopment())
			{
				app.UseSwagger();
				app.UseSwaggerUI();
			}
			
			if(app.Environment.IsDevelopment() == false)
				app.UseHsts();
			app.UseHttpsRedirection();
			app.UseRouting();
			app.Use( async ( context, next ) =>
			{
				if(context.Request.Path.StartsWithSegments( "/api/auth" ))
				{
					context.Response.OnStarting( () =>
					{
						context.Response.Headers.CacheControl = "no-store";
						context.Response.Headers.Pragma = "no-cache";
						return Task.CompletedTask;
					} );
				}

				await next();
			} );
			app.UseHttpMetrics();
			app.UseMiddleware<AuthExceptionMiddleware>();
			app.UseRateLimiter();
			app.MapControllers();
			app.MapGrpcService<LoginTicketGrpcService>();
			app.MapHealthChecks( "/health" );
			app.MapMetrics();

			app.Run();
		}
	}
}
