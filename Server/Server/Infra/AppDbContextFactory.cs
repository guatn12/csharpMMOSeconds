using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Infra
{
	public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
	{
		public AppDbContext CreateDbContext( string[] args)
		{
			// 현재 실행 경로에서 appsettings.json 찾기
			string basePath = Directory.GetCurrentDirectory();
			Console.WriteLine( $"AppDbContextFactory BasePath: {basePath}" );

			var configuration = new ConfigurationBuilder()
				.SetBasePath(basePath)
				.AddJsonFile("appsettings.json", optional:false)
				.AddJsonFile("appsettings.Development.json", optional: true)
				.AddUserSecrets<Program>() // User Secrets 지원
				.Build();

			string connectionString = configuration.GetSection("ServerSettings:Database:ConnectionString").Value;

			if(string.IsNullOrWhiteSpace(connectionString) )
			{
				throw new InvalidOperationException(
					"Database ConnectionString not found. Check appsettings.json or User Secrets." );
			}

			Console.WriteLine( $"Using ConnectionString: {connectionString.Substring( 0, 20 )}..." );

			DbContextOptionsBuilder<AppDbContext> optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
			optionsBuilder.UseNpgsql( connectionString );

			return new AppDbContext( optionsBuilder.Options );
		}
	}
}
