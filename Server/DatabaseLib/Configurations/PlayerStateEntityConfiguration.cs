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
	public class PlayerStateEntityConfiguration : IEntityTypeConfiguration<PlayerStateEntity>
	{
		public void Configure(EntityTypeBuilder<PlayerStateEntity> entity)
		{
			entity.ToTable( "player_state" );
			entity.Property( e => e.Version )
				.HasDefaultValue( 1 )
				.IsConcurrencyToken();
			entity.Property( e => e.StateDataJson )
				.HasColumnType( "jsonb" )
				.HasDefaultValue( "{}" );
			entity.Property( e => e.CreatedAt ).HasDefaultValueSql( "NOW()" );
			entity.Property( e => e.LastUpdated ).HasDefaultValueSql( "NOW()" );

			entity.HasOne( e => e.Player ).WithOne()
				.HasForeignKey<PlayerStateEntity>( e => e.PlayerId )
				.OnDelete( DeleteBehavior.Cascade );

			entity.HasIndex( e => e.PlayerId )
				.IsUnique()
				.HasDatabaseName( "ix_player_state_player_id" );
		}
	}
}
