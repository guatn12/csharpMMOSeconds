using System.Threading.Tasks;

namespace ServerCore
{
	public interface IJob
	{
		ValueTask ExecuteAsync();
		void Clear();
	}
}
