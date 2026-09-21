using DatabaseLib.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseLib.Configurations
{
	public class PlayerEntityConfiguration : IEntityTypeConfiguration<PlayerEntity>
	{
		public void Configure(EntityTypeBuilder<PlayerEntity> entity)
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

			entity.Property( e => e.AccountId )
				.HasColumnName( "account_id" );

			entity.Property( e => e.Level )
			.HasColumnName( "level" )
			.HasDefaultValue( 1 );

			entity.Property( e => e.Experience )
			.HasColumnName( "experience" )
			.HasDefaultValue( 0L );

			entity.Property( e => e.LastEnteredGameAt )
			.HasColumnName( "last_entered_game_at" );

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

			entity.HasIndex( e => e.AccountId )
				.HasDatabaseName( "ix_players_account_id" );

			entity.HasOne<AccountEntity>()
				.WithMany()
				.HasForeignKey( x => x.AccountId )
				.OnDelete( DeleteBehavior.Restrict );

			// JSONB 인덱스 (GIN)
			entity.HasIndex( e => e.PlayerSettingsJson )
			.HasDatabaseName( "ix_players_settings_gin" )
			.HasMethod( "gin" );
		}
	}
}
