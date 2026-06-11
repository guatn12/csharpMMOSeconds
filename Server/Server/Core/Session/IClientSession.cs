using Google.Protobuf;
using Server.Game;
using Server.Room;
using ServerCore;
using System.Threading.Tasks;

namespace Server.Core.Session
{
	public interface IClientSession
	{
		long SessionId { get; }
		long PlayerId { get; }
		long LastActiveTime { get; }
		Player Player { get; }
		IRoom CurrentRoom { get; }
		SessionState State { get; }
		void EnqueueSystemJob( IJob job );
		bool TryTransitionTo( SessionState state );
		void SetCurrentRoom( IRoom room );
		void Send( IMessage packet );
		void Disconnect();
	}
}
