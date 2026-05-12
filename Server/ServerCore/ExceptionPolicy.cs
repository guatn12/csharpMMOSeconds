using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public static class ExceptionPolicy
	{
		public static bool IsCritical(Exception ex)
		{
			return ex is OutOfMemoryException ||
				   ex is StackOverflowException ||
				   ex is AccessViolationException;
		}
	}
}
