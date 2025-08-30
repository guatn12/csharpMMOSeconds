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

		public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) 
		{

		}
	}
}
