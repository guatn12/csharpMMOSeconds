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
	public class AccountEntityConfiguration : IEntityTypeConfiguration<AccountEntity>
	{
		public void Configure(EntityTypeBuilder<AccountEntity> entity)
		{
			entity.ToTable( "accounts" );
			entity.HasKey( x => x.AccountId );

			entity.Property( x => x.AccountId )
				.HasColumnName( "account_id" )
				.ValueGeneratedOnAdd();

			entity.Property( x => x.LoginId )
				.HasColumnName( "login_id" )
				.IsRequired()
				.HasMaxLength( 50 );

			entity.Property(x => x.NormalizedLoginId)
				.HasColumnName( "normalized_login_id" )
				.IsRequired()
				.HasMaxLength( 50 );

			entity.Property(x => x.PasswordHash )
				.HasColumnName( "password_hash" )
				.IsRequired()
				.HasMaxLength( 100 );

			entity.Property( x => x.LastLoginAt )
				.HasColumnName( "last_login_at" );

			entity.Property( x => x.CreatedAt )
				.HasColumnName( "created_at" )
				.HasDefaultValueSql( "NOW()" );

			entity.Property( x => x.UpdatedAt )
				.HasColumnName( "updated_at" )
				.HasDefaultValueSql( "NOW()" );

			entity.HasIndex( x => x.NormalizedLoginId )
				.IsUnique()
				.HasDatabaseName( "ux_accounts_normalized_login_id" );
		}
	}
}
