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
		private Func<Task> _asyncAction;

		public void Initialize( Action action )
		{
			_action = action;
		}

		public void Initialize(Func<Task> asyncAction )
		{
			_asyncAction = asyncAction;
		}

		public async ValueTask ExecuteAsync()
		{
			if(_action != null)
			{
				_action.Invoke();
				return;
			}
			
			if(_asyncAction != null)
			{
				await _asyncAction.Invoke();
			}
		}

		public void Clear()
		{
			_action = null;
			_asyncAction = null;
		}
	}
}
