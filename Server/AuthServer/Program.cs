
using AuthServer.Grpc;
using AuthServer.Options;
using AuthServer.Services;
using DatabaseLib.Extensions;

namespace AuthServer
{
	public class Program
	{
		public static void Main( string[] args )
		{
			var builder = WebApplication.CreateBuilder(args);

			// Add services to the container.
			builder.Services.AddDatabaseLib( builder.Configuration );
			builder.Services.AddOptions<AuthSettings>()
				.Bind( builder.Configuration.GetSection( "AuthSettings" ) )
				.Validate( settings => 0 < settings.LoginTicketTtlSeconds, "AuthSettings:LoginTicketTtlSeconds must be greater than 0." )
				.ValidateOnStart();

			builder.Services.AddSingleton<LoginIdNormalizer>();
			builder.Services.AddSingleton<IPasswordHashService, BCryptPasswordHashService>();
			builder.Services.AddSingleton<LoginTicketService>();
			builder.Services.AddScoped<AccountAuthService>();

			builder.Services.AddControllers();
			// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
			builder.Services.AddGrpc();
			builder.Services.AddProblemDetails();
			builder.Services.AddEndpointsApiExplorer();
			builder.Services.AddSwaggerGen();

			var app = builder.Build();

			// Configure the HTTP request pipeline.
			if(app.Environment.IsDevelopment())
			{
				app.UseSwagger();
				app.UseSwaggerUI();
			}

			app.UseHttpsRedirection();

			//app.UseAuthorization();


			app.MapControllers();
			app.MapGrpcService<LoginTicketGrpcService>();

			app.Run();
		}
	}
}
