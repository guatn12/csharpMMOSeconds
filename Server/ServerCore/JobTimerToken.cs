using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public class JobTimerToken
	{
		public bool IsCanceled { get; private set; }

		public void Cancel()
		{
			IsCanceled = true;
		}
	}
}
