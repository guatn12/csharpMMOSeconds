using Google.Protobuf;
using Server.Data;
using Server.Game;
using Server.Room;
using ServerCore;
using System.Threading.Tasks;

namespace Server.Core.Session
{
	public interface IClientSession
	{
		long SessionId { get; }
		long AccountId { get; }
		long PlayerId { get; }
		long PlayerRawId { get; }
		string LoginToken { get; }
		long LastActiveTime { get; }
		bool IsAuthenticated { get; }
		Player Player { get; }
		IRoom CurrentRoom { get; }
		SessionState State { get; }
		void CreatePlayer( IDataManager dataManager, long playerId, string playerName );
		void SetLoginToken( string token );
		void BindAccountId( long accountId );
		void BindPlayerRawId( long playerRawId );
		void EnqueueSystemJob( IJob job );
		bool TryTransitionTo( SessionState state );
		void SetCurrentRoom( IRoom room );
		void Send( IMessage packet );
		void Disconnect();
		void DisconnectForShutdown();
		Task DisconnectCompletion { get; }
	}
}
