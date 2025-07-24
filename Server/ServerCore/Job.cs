using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public class Job : IJob
	{
		private Action _action;

		public Job(Action action)
		{
			_action = action;
		}

		public void Execute()
		{
			_action.Invoke();
		}
	}
}
