using Microsoft.Extensions.Logging;
using Protocol;
using Server.Extensions;
using Server.Infra;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Services
{
	public class PlayerPositionService
	{
		private readonly IDatabase _redis;
		private readonly ILogger<PlayerPositionService> _logger;


		// 성능 최적화용 상수
		private const int POSITION_EXPIRE_MINUTES = 30;
		private const string POSITION_KEY_PREFIX = "player:position:";
		private const string ACTIVE_PLAYERS_SET = "active_players";

		public PlayerPositionService( IConnectionMultiplexer redis, ILogger<PlayerPositionService> logger )
		{
			_redis = redis.GetDatabase();
			_logger = logger;
		}

		public async Task UpdatePositionAsync( long playerId, PosInfo posInfo )
		{
			try
			{
				string key = $"{POSITION_KEY_PREFIX}{playerId}";

				// Redis Hash로 3D 좌표 저장 (메모리 효율적)
				HashEntry[] positionData = new HashEntry[]
				{
					new("x", posInfo.PosX),
					new("y", posInfo.PosY),
					new("z", posInfo.PosZ),
					new("rotX", posInfo.RotationX),
					new("rotY", posInfo.RotationY),
					new("rotZ", posInfo.RotationZ),
					new("timestamp", posInfo.Timestamp),
					new("lastUpdate", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
				};

				// 배치 실행으로 성능 최적화
				IBatch batch = _redis.CreateBatch();

				// 위치 정보 저장
				_ = batch.HashSetAsync( key, positionData );

				// 만료 시간 설정
				_ = batch.KeyExpireAsync( key, TimeSpan.FromMinutes( POSITION_EXPIRE_MINUTES ) );

				// 활성 플레이어 목록에 추가
				_ = batch.SetAddAsync( ACTIVE_PLAYERS_SET, playerId );

				// 배치 실행 - 위의 내용 일괄 적용.
				batch.Execute();

				_logger.LogDebug( "플레이어 위치 업데이트 {PlayerId}: ({X}, {Y}, {Z} )",
					playerId, posInfo.PosX, posInfo.PosY, posInfo.PosZ );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "플레이어 위치 업데이트 실패 {PlayerId}", playerId );
				throw;
			}
		}

		public async Task<PosInfo> GetPositionAsync( long playerId )
		{
			try
			{
				string key = $"{POSITION_KEY_PREFIX}{playerId}";
				HashEntry[] positionData = await _redis.HashGetAllAsync(key);

				if(positionData.Length == 0)
					return null;

				return new PosInfo
				{
					PosX = positionData.GetFloat( "x" ),
					PosY = positionData.GetFloat( "y" ),
					PosZ = positionData.GetFloat( "z" ),
					RotationX = positionData.GetFloat( "rotX" ),
					RotationY = positionData.GetFloat( "rotY" ),
					RotationZ = positionData.GetFloat( "rotZ" ),
					Timestamp = positionData.GetLong( "timestamp" )
				};
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "플레이어 위치 로드 실패. {PlayerId}", playerId );
				return null;
			}
		}

		public async Task<List<(long PlayerId, PosInfo Position)>> GetNearByPlayersAsync(
			float centerX, float centerY, float centerZ, float radius )
		{
			try
			{
				var nearbyPlayers = new List<(long PlayerId, PosInfo Position)>();

				// 활성 플레이어 목록 조회
				var activePlayerIds = await _redis.SetMembersAsync(ACTIVE_PLAYERS_SET);

				// 배치로 모든 플레이어 위치 조회 (성능 최적화)
				var batch = _redis.CreateBatch();
				var positionTasks = new Dictionary<long, Task<HashEntry[]>>();

				foreach(var playerIdValue in activePlayerIds)
				{
					if(playerIdValue.TryParse( out long playerId ))
					{
						var key = $"{POSITION_KEY_PREFIX}{playerId}";
						positionTasks[ playerId ] = batch.HashGetAllAsync( key );
					}
				}

				batch.Execute();

				// 3D 거리 계산 및 반경 내 플레이어 필터링
				foreach(var kvp in positionTasks)
				{
					var positionData = await kvp.Value;
					if(positionData.Length == 0) continue;

					var playerPos = new PosInfo
					{
						PosX = positionData.GetFloat( "x" ),
						PosY = positionData.GetFloat( "y" ),
						PosZ = positionData.GetFloat( "z" ),
						RotationX = positionData.GetFloat( "rotX" ),
						RotationY = positionData.GetFloat( "rotY" ),
						RotationZ = positionData.GetFloat( "rotZ" ),
						Timestamp = positionData.GetLong( "timestamp" )
					};

					// 3D 거리 계산
					var distance = (float)Math.Sqrt(
						Math.Pow(playerPos.PosX - centerX, 2) +
						Math.Pow(playerPos.PosY - centerY, 2) +
						Math.Pow(playerPos.PosZ - centerZ, 2)
						);

					if (distance <= radius)
					{
						nearbyPlayers.Add( (kvp.Key, playerPos) );
					}
				}

				return nearbyPlayers.OrderBy( x => CalculateDistance3D(
					new PosInfo { PosX = centerX, PosY = centerY, PosZ = centerZ }, x.Position ) ).ToList();
			}
			catch( Exception ex )
			{
				_logger.LogError( ex, "근처 플레이어 탐색 실패. 위치 : ({X}, {Y}, {Z})", centerX, centerY, centerZ );
				return new List<(long, PosInfo)>();
			}
		}

		public async Task RemovePositionAsync(long playerId)
		{
			try
			{
				var key = $"{POSITION_KEY_PREFIX}{playerId}";

				var batch = _redis.CreateBatch();
				_ = batch.KeyDeleteAsync( key );
				_ = batch.SetRemoveAsync( ACTIVE_PLAYERS_SET, playerId );
				batch.Execute();

				_logger.LogDebug( "플레이어 위치 삭제 {PlayerId}", playerId );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "플레이어 위치 삭제 실패. {PlayerId}", playerId );
			}
		}

		public float CalculateDistance3D(PosInfo pos1, PosInfo pos2)
		{
			return (float)Math.Sqrt(
				Math.Pow( pos1.PosX - pos2.PosX, 2 ) +
				Math.Pow( pos1.PosY - pos2.PosY, 2 ) +
				Math.Pow( pos1.PosZ - pos2.PosZ, 2 ) );
		}

		public async Task<bool> UpdatePositionWithValidationAsync(long playerId, PosInfo posInfo, Room.BaseRoom room)
		{
			try
			{
				// 룸 경계 검증
				if(!Utils.Position3DValidator.IsValidPosition(posInfo, room))
				{
					_logger.LogWarning( "Invalid position for player {PlayerId}: ({X}, {Y},{Z}) outside room {RoomId} bounds",
						playerId, posInfo.PosX, posInfo.PosY, posInfo.PosZ, room.RoomId);

					// 위치를 룸 경계 내로 클램핑
					PosInfo clampedPosition = Utils.Position3DValidator.ClampToRoomBounds(posInfo, room);

					// 클램핑된 위치로 업데이트
					await UpdatePositionAsync( playerId, clampedPosition );
					return false; // 원본 위치가 유효하지 않았음을 표시
				}

				// 유효한 위치면 정상 업데이트
				await UpdatePositionAsync( playerId, posInfo );
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError( ex, "플레이어 위치 변경 검증 실패 {PlayerId}", playerId );
				return false;
			}
		}

		public async Task<List<(long PlayerId, PosInfo Position)>> GetNearbyPlayersInRoomAsync(
			long playerId, float radius, Room.BaseRoom room, int maxResults = 50)
		{
			try
			{
				// 현재 플레이어 위치 조회
				PosInfo currentPos = await GetPositionAsync(playerId);
				if(currentPos == null)
					return new List<(long PlayerId, PosInfo Position)>();

				// 룸 내 모든 활성 플레이어 조회
				List<(long PlayerId, PosInfo Position)> allPlayers = await GetNearByPlayersAsync(currentPos.PosX, currentPos.PosY, currentPos.PosZ, radius * 2);

				// 룸 경계 내 플레이어만 필터링  + 거리 재계산
				List<(long PlayerId, PosInfo Position, float Distance)> validPlayers = new();

				foreach( var (otherPlayerId, otherPos) in allPlayers)
				{
					// 현재 플레이어 자신은 제외
					if(otherPlayerId == playerId)
						continue;

					// 룸 경계 내 플레이어만 포함
					if(Utils.Position3DValidator.IsValidPosition(otherPos, room))
					{
						float distance = Utils.Position3DValidator.CalculateDistance3D(currentPos, otherPos);
						if(distance <= radius)
						{
							validPlayers.Add( (otherPlayerId, otherPos, distance) );
						}
					}
				}

				// 거리순 정렬 후 반환
				return validPlayers.OrderBy( p => p.Distance )
					.Take( maxResults )
					.Select( p => (p.PlayerId, p.Position) )
					.ToList();
			}
			catch ( Exception ex )
			{
				_logger.LogError( ex, "룸 내부의 근처 플레이어 가져오기 실패. {PlayerId}", playerId );
				return new List<(long PlayerId, PosInfo Position)>();
			}
		}
	}
}
