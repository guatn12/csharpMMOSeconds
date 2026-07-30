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
	public class GameActivityEntityConfiguration : IEntityTypeConfiguration<GameActivityEntity>
	{
		public void Configure(EntityTypeBuilder<GameActivityEntity> entity)
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
		}
	}
}
