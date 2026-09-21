using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseLib.Entities
{
	public class AccountEntity
	{
		[Key]
		[Column( "account_id")]
		public long AccountId { get; set; }

		[Required]
		[MaxLength(50)]
		[Column("login_id")]
		public string LoginId { get; set; }

		[Required]
		[MaxLength(50)]
		[Column("normalized_login_id")]
		public string NormalizedLoginId { get; set; }

		[Required]
		[MaxLength(100)]
		[Column( "password_hash" )]
		public string PasswordHash { get; set; }

		[Column("last_login_at")]
		public DateTime? LastLoginAt { get; set; }

		[Column("created_at")]
		public DateTime CreatedAt { get; set; }

		[Column("updated_at")]
		public DateTime UpdatedAt { get; set; }

	}
}
