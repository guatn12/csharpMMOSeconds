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
		public DbSet<InventoryEntity> Inventory { get; set; }
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

				entity.Property( e => e.Experience )
				.HasColumnName( "experience" )
				.HasDefaultValue( 0L );

				entity.Property( e => e.LoginToken )
				.HasColumnName( "login_token" )
				.HasMaxLength( 255 );

				entity.Property( e => e.LastLoginAt )
				.HasColumnName( "last_login_at" );

				entity.Property( e => e.TotalPlayTimeMinutes )
				.HasColumnName( "total_play_time_minutes" )
				.HasDefaultValue( 0L );

				entity.Property( e => e.CreatedAt )
				.HasColumnName( "created_at" )
				.HasDefaultValueSql( "NOW()" );

				entity.Property( e => e.UpdatedAt )
				.HasColumnName( "updated_at" )
				.HasDefaultValueSql( "NOW()" );

				// JSONB 설정
				entity.Property( e => e.PlayerSettingsJson )
				.HasColumnName( "player_settings" )
				.HasColumnType( "jsonb" )
				.HasDefaultValue( "{}" );

				// 인덱스 추가
				entity.HasIndex( e => e.PlayerName )
				.IsUnique()
				.HasDatabaseName( "ix_players_player_name" );

				entity.HasIndex( e => e.CreatedAt )
				.HasDatabaseName( "ix_players_created_at" );

				entity.HasIndex( e => e.LoginToken )
				.HasDatabaseName( "ix_players_login_token" );

				// JSONB 인덱스 (GIN)
				entity.HasIndex( e => e.PlayerSettingsJson )
				.HasDatabaseName( "ix_players_settings_gin" )
				.HasMethod( "gin" );
			} );

			// InventoryEntity 구성
			modelBuilder.Entity<InventoryEntity>( entity =>
			{
				entity.ToTable( "inventory" );
				entity.HasKey( e => e.InventoryId );

				entity.Property( e => e.InventoryId )
				.HasColumnName( "inventory_id" )
				.ValueGeneratedOnAdd();

				entity.Property( e => e.PlayerId )
				.HasColumnName( "player_id" )
				.IsRequired();

				entity.Property( e => e.MaxSlots )
				.HasColumnName( "max_slots" )
				.HasDefaultValue( 50 );

				entity.Property( e => e.Version )
				.HasColumnName( "version" )
				.HasDefaultValue( 1 )
				.IsConcurrencyToken();      // 낙관적 동시성 제어

				entity.Property( e => e.LastUpdated )
				.HasColumnName( "last_updated" )
				.HasDefaultValueSql( "NOW()" );

				entity.Property( e => e.CreatedAt )
				.HasColumnName( "created_at" )
				.HasDefaultValueSql( "NOW()" );

				// JSONB 설정
				entity.Property( e => e.InventoryDataJson )
				.HasColumnName( "inventory_data" )
				.HasColumnType( "jsonb" )
				.HasDefaultValue( "{}" );

				// 관계 설정 (1:1 Player - Inventory)
				entity.HasOne( e => e.Player )
				.WithOne()
				.HasForeignKey<InventoryEntity>( e => e.PlayerId )
				.OnDelete( DeleteBehavior.Cascade );

				// 인덱스 설정
				entity.HasIndex( e => e.PlayerId )
				.IsUnique()
				.HasDatabaseName( "ix_inventory_player_id" );

				entity.HasIndex( e => e.LastUpdated )
				.HasDatabaseName( "ix_inventory_last_updated" );

				// JSONB 인덱스 (아이템 검색용)
				entity.HasIndex( e => e.InventoryDataJson )
				.HasDatabaseName( "ix_inventory_data_gin" )
				.HasMethod( "gin" );
			} );

			// GameActivityEntity 구성
			modelBuilder.Entity<GameActivityEntity>( entity =>
			{
				entity.ToTable( "game_activity" );
				entity.HasKey( e => e.ActivityId );

				entity.Property( e => e.ActivityId )
				.HasColumnName( "activity_id" )
				.ValueGeneratedOnAdd();

				entity.Property( e => e.PlayerId )
				.HasColumnName( "player_id" )
				.IsRequired();

				entity.Property( e => e.ActivityType )
				.HasColumnName( "activity_type" )
				.IsRequired()
				.HasMaxLength( 50 );

				entity.Property( e => e.CreatedAt )
				.HasColumnName( "created_at" )
				.HasDefaultValueSql( "NOW()" );

				// JSONB 설정
				entity.Property( e => e.ActivityDataJson )
				.HasColumnName( "activity_data" )
				.HasColumnType( "jsonb" )
				.HasDefaultValue( "{}" );

				// 관계 설정 (N:1 Activity-player)
				entity.HasOne( e => e.Player )
				.WithMany()
				.HasForeignKey( e => e.PlayerId )
				.OnDelete( DeleteBehavior.Cascade );

				// 인덱스 설정 (분석 쿼리 최적화)
				entity.HasIndex( e => e.PlayerId )
				.HasDatabaseName( "ix_game_activity_player_id" );

				entity.HasIndex( e => new { e.ActivityType, e.CreatedAt } )
				.HasDatabaseName( "ix_game_activity_type_data" );

				entity.HasIndex( e => new { e.PlayerId, e.CreatedAt } )
				.HasDatabaseName( "ix_game_activity_player_data" );

				// JSONB 인덱스 (활동 데이터 검색용)
				entity.HasIndex( e => e.ActivityDataJson )
				.HasDatabaseName( "ix_activity_data_gin" )
				.HasMethod( "gin" );
			} );
		}
	}
}
