using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public class DelegateJob : IJob
	{
		private Action _action;

		public void Initialize( Action action )
		{
			_action = action;
		}

		public void Execute()
		{
			_action.Invoke();
		}

		public void Clear()
		{
			_action = null;
		}
	}
}
