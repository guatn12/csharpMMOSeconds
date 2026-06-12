using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace DatabaseLib
{
	/// <summary>
	/// design-time 전용 팩토리. dotnet ef 도구가 모델을 읽어 마이그레이션을 생성/적용할 때만 사용
	/// 런타임 DI는 AddDatabaseLib(호스트 Options 주입) 경로이며 이 팩토리와 무관.
	/// </summary>
	public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
	{
		public AppDbContext CreateDbContext( string[] args)
		{
			// 우선순위 MMO_DB(CI/Override) -> 동봉 design-time json(개발기본) -> 더미
			// migration add는 실 db 무연결이라 더미만으로 동작. 실 적용 시 1/2 누락이면 더미로 연결 시도 -> 즉시 실패
			string connectionString = Environment.GetEnvironmentVariable("MMO_DB")
				?? ReadDesignTimeConnectionString()
				?? "Host=localhost;Port=5432;Database=mmo_design;Username=postgres;Password=postgres";

			DbContextOptionsBuilder<AppDbContext> optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
			optionsBuilder.UseNpgsql( connectionString, npgsql =>
			{
				npgsql.MigrationsAssembly( "DatabaseLib" );
			} );

			return new AppDbContext( optionsBuilder.Options );
		}

		private static string ReadDesignTimeConnectionString()
		{
			var path = Path.Combine(AppContext.BaseDirectory, "dbcontext.designtime.json");
			if(!File.Exists( path )) return null;
			using var doc = JsonDocument.Parse(File.ReadAllText(path));
			return doc.RootElement.TryGetProperty( "ConnectionString", out var v ) ? v.GetString() : null;
		}
	}
}
