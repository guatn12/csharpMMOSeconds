using Microsoft.EntityFrameworkCore;
using Server.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Infra
{
	public class AppDbContext : DbContext
	{
		public DbSet<PlayerEntity> Players { get; set; }

		public AppDbContext( DbContextOptions<AppDbContext> options ) : base( options )
		{

		}

		protected override void OnModelCreating( ModelBuilder modelBuilder )
		{
			base.OnModelCreating( modelBuilder );

			// PlayerEntity 구성
			modelBuilder.Entity<PlayerEntity>( entity =>
			{
				entity.ToTable( "players" );        // 소문자 테이블명 (postgreSQL 관례)
				entity.HasKey( e => e.PlayerId );

				entity.Property( e => e.PlayerId )
				.HasColumnName( "player_id" )
				.ValueGeneratedOnAdd();

				entity.Property( e => e.PlayerName )
				.HasColumnName( "player_name" )
				.IsRequired()
				.HasMaxLength( 50 );

				entity.Property( e => e.Level )
				.HasColumnName( "level" )
				.HasDefaultValue( 1 );

				entity.Property( e => e.CreatedAt )
				.HasColumnName( "created_at" )
				.HasDefaultValueSql( "NOW()" );

				// 인덱스 추가
				entity.HasIndex( e => e.PlayerName )
				.IsUnique()
				.HasDatabaseName( "ix_players_player_name" );

				entity.HasIndex( e => e.CreatedAt )
				.HasDatabaseName( "ix_players_created_at" );
			} );
		}
	}
}
