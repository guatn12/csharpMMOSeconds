using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Protocol;
using Server.Game;
using Server.Room;
using ServerCore;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;

namespace Server
{
    public class GameSession : Session
    {
        private readonly ILogger<GameSession> _logger;
        private readonly IRoomManager _roomManager;
        private IRoom _currentRoom;
        private readonly object _roomLock = new object();

        public IRoom CurrentRoom
        {
            get
            {
                lock(_roomLock)
                {
                    return _currentRoom;
                }
            }
            private set
            {
                lock(_roomLock)
                {
                    _currentRoom = value;
                }
            }
        }
		public bool IsInRoom => _currentRoom != null;

		public int SessionId { get; private set; }
        public Player Player { get; private set; }
        public string PlayerName => Player.PlayerName ?? $"Player_{Player.PlayerId}";
        public long PlayerId => Player.PlayerId;

        private static int _nextSessionId = 1;

        public GameSession( ILogger<GameSession> logger, IRoomManager roomManager )
        {
            _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
            _roomManager = roomManager ?? throw new ArgumentNullException( nameof( _roomManager ) );
        }

        private static int GenerateNextSessionId()
        {
            return System.Threading.Interlocked.Increment( ref _nextSessionId );
        }

		public void Send( IMessage packet )
		{
			ArraySegment<byte> segment = Program.PacketManagerInstance.MakeSendPacket(packet);
			base.Send( segment );
		}

		public override void OnRecvPacket( ArraySegment<byte> buffer )
        {
            ushort packetIdValue = BitConverter.ToUInt16(buffer.Array, buffer.Offset + 2);
            PacketID packetId = (PacketID)packetIdValue;

            _logger.LogDebug( "Packet Received. SessionId: {SessionId}, PacketID: {PacketID}, Size: {Size}",
                this.SessionId, packetId, buffer.Count );

            if(Program.PacketManagerInstance != null)
            {
                Task.Run( async () =>
                {
                    await Program.PacketManagerInstance.HandlePacket( this, buffer );
                } );
            }

            //Program.PacketManagerInstance.HandlePacket( this, buffer );
        }

        public override void OnSend( int bytes )
        {
            //LogManager.Debug("Packet Sent. SessionId: {SessionId}, Size: {Size}", this.SessionId, bytes);
            _logger.LogDebug( "Packet Sent. SessionId: {SessionId}, Size: {Size}", this.SessionId, bytes );
        }

		// 비동기 패킷 전송
		public async Task SendAsync( IMessage packet )
		{
			await Task.Run( () => Send( packet ) );
		}

		public override void OnConnected( EndPoint endPoint )
		{
            this.SessionId = GenerateNextSessionId();
            //LogManager.Info("Client Connected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint);
			_logger.LogInformation( "Client Connected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint );

            // 플레이어 정보 초기화.
            InitializePlayer();

			// 기본 로비에 자동 입장 시도.
			_ = Task.Run( async () => await TryJoinDefaultLobbyAsync() );
		}

		public override void OnDisConnected( EndPoint endPoint )
		{
			//LogManager.Info("Client Disconnected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint);
			_logger.LogInformation( "Client Disconnected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint );

			// 플레이어 상태를 Disconnected상태로 처리
			Player?.Disconnect();

			// 모든 룸에서 퇴장 처리
			_ = Task.Run( async () => await LeaveAllRoomsAsync() );
		}

		// 기본 로비 입장 시도
		public async Task<bool> TryJoinDefaultLobbyAsync()
        {
            try
            {
                _logger.LogDebug( "Attempting to join default lobby for Player {SessionId}", SessionId );

                RoomEnterResult result = await _roomManager.JoinDefaultLobbyAsync(this);
                if(result == RoomEnterResult.Success)
                {
                    // currentRoom은 RoomManager.JoinDefaultLobbyAsync 내부에서 설정됨
                    IRoom lobby = await _roomManager.FindPlayerCurrentRoomAsync(this);
                    CurrentRoom = lobby;

                    S_EnterGame enterPacket = new S_EnterGame
                    {
                        Player = Player.Info
                    };
                    
                    await SendAsync( enterPacket );

                    _logger.LogInformation( "Player {SessionId} successfully joined default lobby (Room: {RoomId})",
                        SessionId, lobby?.RoomId );
                    return true;
                }
                else
                {
                    _logger.LogWarning( "Player {SessionId} failed to join default lobby: {Result}",
                        SessionId, result );
                    return false;
                }
            }
            catch(Exception ex)
            {
                _logger.LogError( ex, "Error joining default lobby for Player {SessionId}", SessionId );
                return false;
            }
        }

        // 특정 룸으로 이동
        public async Task<RoomEnterResult> MoveToRoomAsync( int roomId )
        {
            try
            {
                _logger.LogDebug( "Player {SessionId} attempting to move to Room {RoomId}", SessionId, roomId );

                IRoom previousRoom = CurrentRoom;
                RoomEnterResult result = await _roomManager.MovePlayerToRoomAsync(this, roomId);

                if(result == RoomEnterResult.Success)
                {
                    IRoom newRoom = await _roomManager.FindPlayerCurrentRoomAsync(this);
                    CurrentRoom = newRoom;

                    _logger.LogInformation( "Player {SessionId} moved from Room {PreviousRoomId} to Room {NewRoomId}",
                          SessionId, previousRoom?.RoomId, newRoom?.RoomId );
                }

                return result;
            }
            catch(Exception ex)
            {
                _logger.LogError( ex, "Error moving Player {SessionId} to Room {RoomId}", SessionId, roomId );
                return RoomEnterResult.UnknownError;
            }
        }

        // 현재 룸에서 퇴장
        public async Task<bool> LeaveCurrentRoomAsync()
        {
            IRoom room = CurrentRoom;
            if(room == null)
            {
                _logger.LogDebug( "Player {SessionId} is not in any room", SessionId );
                return false;
            }

            try
            {
                _logger.LogDebug( "Player {SessionId} leaving Room {RoomId}", SessionId, room.RoomId );

                bool success = await room.TryLeaveAsync(this);
                if(success)
                {
                    CurrentRoom = null;
                    _logger.LogInformation( "Player {SessionId} left Room {RoomId}", SessionId, room.RoomId );
                }

                return success;
            }
            catch(Exception ex)
            {
                _logger.LogError( ex, "Error leaving room for Player {SessionId}", SessionId );
                return false;
            }
        }

        // 모든 룸에서 퇴장 (연결 해제 시 호출)
        public async Task LeaveAllRoomsAsync()
        {
            try
            {
                bool success = await _roomManager.RemovePlayerFromAllRoomsAsync(this);
                if(success)
                {
                    CurrentRoom = null;
                    _logger.LogInformation( "Player {SessionId} removed from all rooms", SessionId );
                }
            }
            catch(Exception ex)
            {
                _logger.LogError( ex, "Error removing Player {SessionId} from all rooms", SessionId );
            }
        }

        // 플레이어 초기화
        private void InitializePlayer()
        {
			Player = new Player( SessionId, null );

            _logger.LogInformation( "Player initialized: {PlayerInfo}", Player.ToString() );
        }

        // 플레이어 상태 업데이트
        public void UpdatePosition(PosInfo newPosition)
        {
            if(Player == null) return;

            Player.UpdatePosition(newPosition);
        }

        public bool TakeDamage( int damage)
        {
            if(Player == null) return false;

            bool result = Player.TakeDamage(damage);
            if(result)
            {
                if(Player.State == PlayerState.Dead)
                {
                    // TODO : 플레이어 Dead 상태 전달 필요.
                }
            }

            return result;
        }

        public bool Heal(int amount)
        {
            if(Player == null) return false;

            return Player.Heal(amount);
        }

        public bool GainExperience(long exp)
        {
            if(Player == null) return false;

            return Player.GainExperience(exp);
        }

        // 현재 상태 정보 조회
        public GameSessionInfo GetSessionInfo()
        {
            return new GameSessionInfo
            {
                SessionId = SessionId,
                PlayerName = PlayerName,
                IsInRoom = IsInRoom,
                CurrentRoomId = CurrentRoom?.RoomId,
                CurrentRoomName = CurrentRoom?.RoomName
            };
        }

        // 전체 플레이어 정보 반환 메서드
        public PlayerInfo GetPlayerFullInfo()
        {
            return Player?.Info;
        }

		// 디버깅용
		public override string ToString()
		{
			return $"GameSession(Id: {SessionId}, Room: {CurrentRoom?.RoomId})";
		}
	}

    public class GameSessionInfo
    {
        public int SessionId {  get; set; }
        public string PlayerName { get; set; }
        public bool IsInRoom { get; set; }
        public int? CurrentRoomId { get; set; }
        public string CurrentRoomName { get; set; }
    }
}
