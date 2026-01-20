using Google.Protobuf;
using Server.Game;
using Server.Room;
using System.Threading.Tasks;

namespace Server.Core.Session
{
	public interface IClientSession
	{
		long SessionId { get; }
		long PlayerId { get; }
		Player Player { get; }
		IRoom CurrentRoom { get; }
		void Send( IMessage packet );
		Task SendAsync( IMessage packet );
	}
}
