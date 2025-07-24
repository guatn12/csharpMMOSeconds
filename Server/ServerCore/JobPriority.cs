using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerCore
{
	public enum JobPriority
	{
		// 숫자가 낮을 수록 높은 우선 순위.
		High = 0,
		Medium = 1,
		Low = 2,
		VeryLow = 3
	}
}
