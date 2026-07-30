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
	public class EquipmentEntityConfiguration : IEntityTypeConfiguration<EquipmentEntity> 
	{
		public void Configure(EntityTypeBuilder<EquipmentEntity> entity)
		{
			entity.ToTable( "equipment" );
			entity.Property( e => e.Version ).HasDefaultValue( 1 ).IsConcurrencyToken();    // 낙관적 동시성
			entity.Property( e => e.LastUpdated ).HasDefaultValueSql( "NOW()" );
			entity.Property( e => e.CreatedAt ).HasDefaultValueSql( "NOW()" );
			entity.Property( e => e.EquipmentDataJson ).HasColumnType( "jsonb" ).HasDefaultValue( "{}" );

			entity.HasOne( e => e.Player ).WithOne()
				.HasForeignKey<EquipmentEntity>( e => e.PlayerId )
				.OnDelete( DeleteBehavior.Cascade );

			entity.HasIndex( e => e.PlayerId ).IsUnique()
				.HasDatabaseName( "ix_equipment_player_id" );
		}
	}
}
