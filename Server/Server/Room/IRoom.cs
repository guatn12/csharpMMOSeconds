using Google.Protobuf;
using Protocol;
using Server.Core.Session;
using Server.Game.Map;
using Server.Packet.Handlers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Server.Room
{
	public interface IRoom
	{
		#region 기본 정보
		int RoomId { get; }
		string RoomName { get; }
		int MaxPlayers { get; }
		int CurrentPlayerCount { get; }

		RoomType RoomType { get; }
		RoomState State { get; }

		GameMap RoomMap { get; }

		// Category 핸들러
		RoomPacketHandler RoomPacketHandler { get; }
		CombatPacketHandler CombatPacketHandler { get; }
		InventoryPacketHandler InventoryPacketHandler { get; }
		#endregion

		// 현재 룸에 있는 플레이어 리스트 (읽기 전용)
		IReadOnlyList<IClientSession> Players { get; }
		// 플레이어 확인
		bool ContainsPlayer(IClientSession session);
		// playerId를 통한 플레이어 확인
		bool ContainsPlayerToPlayerId(long playerId);
		// 외부에서 Queue를 통한 플레이어 룸 입장 시도
		Task<RoomEnterResult> EnterViaQueueAsync(IClientSession session );
		// 외부에서 Queue를 통한 플레이어 룸 퇴장 시도
		Task<bool> LeaveViaQueueAsync( IClientSession session );
		// sessionId를 통해 플레이어 확인
		IClientSession FindPlayer( int sessionId );
		// playerId를 통한 플레이어 확인
		IClientSession FindPlayerToPlayerId(long playerId);
		// 룸 내 모든 플레이어에게 브로드캐스트
		void Broadcast( IMessage packet, IClientSession excludeSession = null );
		// 룸 내 특정 범위 내 플레이어에게 브로드캐스트
		void BroadcastInRange( IMessage packet, PosInfo position, IClientSession excludeSession = null );
		// 룸 내 특정 플레이어에게 전달
		void SendToPlayer( IClientSession session, IMessage packet );
		// Worker 컨텍스트에서 action을 delayMs 후에 실행하도록 예약
		void ScheduleJob(Action action, int delayMs );
		// Worker 컨텍스트에서 asyncAction을 delayMs 후에 실행하도록 예약
		void ScheduleJob(Func<Task> asyncAction, int delayMs);
		// 룸의 주기적 업데이트 - 활성화 상태 유지를 위한 tick
		void Tick();    
		//룸 초기화
		Task InitializeAsync();
		// 룸 정리 및 종료
		Task CleanupAsync();

		bool IsEmpty { get; }	// 룸이 비어 있는지 확인
		bool IsFull { get; }    // 룸이 가득 찼는지 확인

		///<summary> 플레이어 입장 이벤트 - 외부(로깅, 퀘스트 등)에서 구독 가능 </summary> 
		event EventHandler<PlayerRoomEventArgs> PlayerEntered;
		/// <summary> 플레이어 퇴장 이벤트 - 외부(로깅, 퀘스트 등)에서 구독 가능 </summary>
		event EventHandler<PlayerRoomEventArgs> PlayerLeft;
	}

	#region 열거형 및 결과 클래스
	//public enum RoomType
	//{
	//	Lobby = 0,		// 로비 룸
	//	Battle = 1,		// 전투 룸
	//	Dungeon = 2,	// 던전 룸
	//	Guild = 3,		// 길드 룸
	//	Private	= 4,	// 개인 룸
	//}

	public enum RoomState
	{
		Created = 0,	// 생성됨
		Active = 1,		// 활성 상태
		Full = 2,		// 가득 참
		Closing = 3,	// 종료 중
		Closed = 4,		// 종료됨
	}

	public enum RoomEnterResult
	{
		Success = 0,		// 성공
		RoomFull = 1,		// 룸이 가득 참
		AlreadyInRoom = 2,	// 이미 룸에 있음
		RoomClosed = 3,		// 룸이 닫힘
		InvalidState = 4,	// 잘못된 상태
		UnknownError = 5,	// 알 수 없는 오류
	}

	/// <summary>
	/// 플레이어 룸 이벤트 인자
	/// </summary>
	public class PlayerRoomEventArgs : EventArgs
	{
		public IClientSession Player { get; }
		public IRoom Room { get; }
		public DateTime Timestamp { get; }

		public PlayerRoomEventArgs(IClientSession player, IRoom room)
		{
			Player = player ?? throw new ArgumentNullException(nameof(player));
			Room = room ?? throw new ArgumentNullException( nameof( room ) );
			Timestamp = DateTime.UtcNow;
		}
	}
	#endregion
}
