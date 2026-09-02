using Microsoft.EntityFrameworkCore;
using DatabaseLib.Entities;

namespace DatabaseLib
{
	public class AppDbContext : DbContext
	{
		public DbSet<PlayerEntity> Players { get; set; }
		public DbSet<PlayerStateEntity> PlayerState { get; set; }
		public DbSet<InventoryEntity> Inventory { get; set; }
		public DbSet<EquipmentEntity> Equipment { get; set; }
		public DbSet<GameActivityEntity> GameActivity { get; set; }

		public AppDbContext( DbContextOptions<AppDbContext> options ) : base( options )
		{

		}

		protected override void OnModelCreating( ModelBuilder modelBuilder )
		{
			base.OnModelCreating( modelBuilder );

			// postgreSQL 네이밍 규칙 적용(snake_case)
			foreach(var entity in modelBuilder.Model.GetEntityTypes())
			{
				entity.SetTableName( entity.GetTableName()?.ToLowerInvariant() );
				foreach(var property in entity.GetProperties() )
				{
					property.SetColumnName( property.GetColumnName().ToLowerInvariant() );
				}
			}

			// PlayerEntity 구성
			// InventoryEntity 구성
			// GameActivityEntity 구성
			// EquipmentEntity 구성
			// PlayerState 구성
			modelBuilder.ApplyConfigurationsFromAssembly( typeof( AppDbContext ).Assembly );
		}
	}
}
