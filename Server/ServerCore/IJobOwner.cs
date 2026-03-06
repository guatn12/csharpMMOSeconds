using System.Threading.Tasks;

namespace ServerCore
{
	public interface IJobOwner
	{
		ValueTask ProcessJobsAsync();
	}
}
