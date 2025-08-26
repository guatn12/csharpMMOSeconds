using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Protocol;
using Server.Room;
using ServerCore;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;

namespace Server
{
    public class GameSession : Session, IJobOwner
    {
        private readonly ILogger<GameSession> _logger;
        private readonly IRoomManager _roomManager;
        private IRoom _currentRoom;
        private readonly object _roomLock = new object();

        public int SessionId { get; private set; }
        public ConcurrentQueue<IJob> JobQueue { get; } = new ConcurrentQueue<IJob>();

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
        public string PlayerName => $"Player_{SessionId}";

        public GameSession( ILogger<GameSession> logger, IRoomManager roomManager )
        {
            _logger = logger ?? throw new ArgumentNullException( nameof( logger ) );
            _roomManager = roomManager ?? throw new ArgumentNullException( nameof( _roomManager ) );
        }

        public void Send( IMessage packet )
        {
            ArraySegment<byte> segment = Program.PacketManagerInstance.MakeSendPacket(packet);
            base.Send( segment );
        }

        public override void OnConnected( EndPoint endPoint )
        {
            this.SessionId = GetHashCode(); // 임시 세션 ID 발급
            //LogManager.Info("Client Connected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint);
            _logger.LogInformation( "Client Connected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint );
            // TODO : 클라이언트에게 입장 패킷 전송.

            // 기본 로비에 자동 입장 시도.
            _ = Task.Run( async () => await TryJoinDefaultLobbyAsync() );
        }

        public override void OnDisConnected( EndPoint endPoint )
        {
            //LogManager.Info("Client Disconnected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint);
            _logger.LogInformation( "Client Disconnected. SessionId: {SessionId}, RemoteEndPoint: {RemoteEndPoint}", this.SessionId, endPoint );

            // 모든 룸에서 퇴장 처리
            _ = Task.Run( async () => await LeaveAllRoomsAsync() );
        }

        public override void OnRecvPacket( ArraySegment<byte> buffer )
        {
            ushort packetIdValue = BitConverter.ToUInt16(buffer.Array, buffer.Offset + 2);
            PacketID packetId = (PacketID)packetIdValue;

            _logger.LogDebug( "Packet Received. SessionId: {SessionId}, PacketID: {PacketID}, Size: {Size}",
                this.SessionId, packetId, buffer.Count );

            // JobQueue에 작업을 넣기 전 작업 개수
            int prevJobCount = JobQueue.Count;

            JobQueue.Enqueue( new Job( () =>
            {
                Program.PacketManagerInstance.HandlePacket( this, buffer );
            } ) );

            // 큐에 작업을 넣은 후, 만약 큐가 비어있다가(0개) 처음으로 작업이 추가된(1개) 상황이라면
            // JobQueueManager에게 "이 세션에서 처리할 작업이 생겼다"고 알려줍니다.
            if(prevJobCount == 0)
            {
                //JobQueueManager.Instance.Push( this );
                _ = JobQueueManager.Instance.PushAsync( this );
            }
        }

        public override void OnSend( int bytes )
        {
            //LogManager.Debug("Packet Sent. SessionId: {SessionId}, Size: {Size}", this.SessionId, bytes);
            _logger.LogDebug( "Packet Sent. SessionId: {SessionId}, Size: {Size}", this.SessionId, bytes );
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
                        Player = new PlayerInfo {PlayerId = this.SessionId, Name = PlayerName},
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

        // 비동기 패킷 전송
        public async Task SendAsync( IMessage packet )
        {
            await Task.Run( () => Send( packet ) );
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
                CurrentRoomName = CurrentRoom?.RoomName,
                JobQueueCount = JobQueue.Count
            };
        }

		// 디버깅용
		public override string ToString()
		{
			return $"GameSession(Id: {SessionId}, Room: {CurrentRoom?.RoomId}, Jobs: {JobQueue.Count})";
		}

        private bool ShouldRouteToRoom(PacketID packetId)
        {
            return packetId switch
            {
                PacketID.C_Move => true,
                PacketID.C_Chat => true,

                PacketID.S_EnterGame => false,
                PacketID.S_LeaveGame => false,
                PacketID.S_Spawn => false,
                PacketID.S_Despawn => false,
                PacketID.S_Move => false,
                PacketID.S_Chat => false,

                _ => false
            };
        }

        // 패킷을 Room Job Queue로 라우팅
        private void RoutePacketToRoom(PacketID packetId, ArraySegment<byte> buffer)
        {
            IRoom room = CurrentRoom;
            if(room == null )
            {
				_logger.LogWarning( "Player {SessionId} received room packet {PacketID} but is not in any room",
                    SessionId, packetId );
				return;
			}

            try
            {
                // 패킷 타입별 room job 생성
                IJob roomJob = CreateRoomJob(packetId, buffer, room);
                if(roomJob == null )
                {
					_logger.LogWarning( "Failed to create room job for packet {PacketID} from Player {SessionId}",
                        packetId, SessionId );
                    return;
				}

                int prevJobCount = room.JobQueue.Count;
                room.JobQueue.Enqueue( roomJob );

                // Room Job Queue가 비어있었다면 JobQueueManager에 알림
                if(prevJobCount == 0)
                {
                    _ = JobQueueManager.Instance.PushAsync( room );
                }

				_logger.LogDebug( "Packet {PacketID} routed to Room {RoomId} for Player {SessionId}",
                    packetId, room.RoomId, SessionId );
			}
            catch(Exception ex)
            {
				_logger.LogError( ex, "Error routing packet {PacketID} to room for Player {SessionId}",
                    packetId, SessionId );
			}
        }

        // 패킷을 GameSesion Job Queue로 라우팅
        private void RoutePacketToSession(PacketID packetId, ArraySegment<byte> buffer)
        {
            try
            {
                // JobQueue에 작업을 넣기 전 작업 개수
                int prevJobCount = JobQueue.Count;

                JobQueue.Enqueue( new Job( () =>
                {
                    Program.PacketManagerInstance.HandlePacket( this, buffer );
                } ) );

                // 큐에 작업을 넣은 후, 만약 큐가 비어있다가(0개) 처음으로 작업이 추가된(1개) 상황이라면,
                // JobQueueManager에게 "이 세션에서 처리할 작업이 생겼다"고 알려줍니다.
                if(prevJobCount == 0)
                {
                    _ = JobQueueManager.Instance.PushAsync( this );
                }

				_logger.LogDebug( "Packet {PacketID} routed to Session for Player {SessionId}",
                    packetId, SessionId );
			}
            catch(Exception ex)
            {
				_logger.LogError( ex, "Error routing packet {PacketID} to session for Player {SessionId}",
                    packetId, SessionId );
			}
        }

        // 패킷 타입에 따른 Room Job 생성
        private IJob CreateRoomJob(PacketID packetId, ArraySegment<byte> buffer, IRoom room)
        {
            try
            {
                return packetId switch
                {
                    PacketID.C_Move => CreateMoveJob( buffer, room ),
                    PacketID.C_Chat => CreateChatJob( buffer, room ),
                    _ => null
                };
            }
            catch(Exception ex)
            {
				_logger.LogError( ex, "Error creating room job for packet {PacketID} from Player {SessionId}",
                    packetId, SessionId );
				return null;
			}
        }

        // Move Job 생성
        private IJob CreateMoveJob(ArraySegment<byte> buffer, IRoom room)
        {
            try
            {
                Protocol.C_Move movePacket = new Protocol.C_Move();
                movePacket.MergeFrom( buffer.Array, buffer.Offset + 4, buffer.Count - 4 );

                return new Server.Room.Jobs.MoveJob( this, room, movePacket, _logger );
            }
            catch(Exception ex)
            {
				_logger.LogError( ex, "Failed to create MoveJob for Player {SessionId}", SessionId );
				return null;
			}
        }

        // Chat Job 생성
        private IJob CreateChatJob(ArraySegment<byte> buffer, IRoom room)
        {
            try
            {
                Protocol.C_Chat chatPacket = new Protocol.C_Chat();
                chatPacket.MergeFrom( buffer.Array, buffer.Offset + 4, buffer.Count - 4 );

                return new Server.Room.Jobs.ChatJob( this, room, chatPacket, _logger );
            }
            catch (Exception ex)
            {
				_logger.LogError( ex, "Failed to create ChatJob for Player {SessionId}", SessionId );
				return null;
			}
        }
	}

    public class GameSessionInfo
    {
        public int SessionId {  get; set; }
        public string PlayerName { get; set; }
        public bool IsInRoom { get; set; }
        public int? CurrentRoomId { get; set; }
        public string CurrentRoomName { get; set; }
        public int JobQueueCount { get; set; }
    }
}
