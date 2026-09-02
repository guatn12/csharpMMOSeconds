using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatabaseLib.Persistence
{
	public interface IDatabaseWriteCommand<TResult>
	{
		string Name { get; }

		ValueTask<DatabaseWriteDecision> ApplyAsync( AppDbContext context, CancellationToken cancellationToken );
		TResult BuildResultAfterSave();
	}
}
