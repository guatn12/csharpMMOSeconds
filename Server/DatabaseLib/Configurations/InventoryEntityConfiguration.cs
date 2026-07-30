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
	public class InventoryEntityConfiguration : IEntityTypeConfiguration<InventoryEntity>
	{
		public void Configure(EntityTypeBuilder<InventoryEntity> entity)
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
		}
	}
}
