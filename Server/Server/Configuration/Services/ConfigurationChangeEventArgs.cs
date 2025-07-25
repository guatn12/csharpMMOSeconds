using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Configuration.Services
{
	public class ConfigurationChangeEventArgs : EventArgs
	{
		public string SectionName { get; }
		public object OldValue { get; }
		public object NewValue { get; }
		public DateTime ChangeTime { get; }
		public string ChangeReason { get; }

		public ConfigurationChangeEventArgs(
			string sectionName,
			object oldValue,
			object newValue,
			string changeReason = "Configuration file changed" )
		{
			SectionName = sectionName;
			OldValue = oldValue;
			NewValue = newValue;
			ChangeReason = changeReason;
			ChangeTime = DateTime.UtcNow;
		}
	}
}
