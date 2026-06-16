using DatabaseLib.Redis;
using Microsoft.Extensions.Logging;
using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;

namespace Server.Services
{
	public class PlayerPositionService : IPlayerPositionService
	{
		private readonly IRedisService _redis;
		private readonly ILogger<PlayerPositionService> _logger;


		// 성능 최적화용 상수
		private const int POSITION_EXPIRE_MINUTES = 30;
		private const string POSITION_KEY_PREFIX = "player:position:";
		private const string ACTIVE_PLAYERS_SET = "active_players";

		public PlayerPositionService( IRedisService redis, ILogger<PlayerPositionService> logger )
		{
			_redis = redis;
			_logger = logger;
		}

		public async Task UpdatePositionAsync( long playerId, PosInfo posInfo )
		{
			try
			{
				string key = $"{POSITION_KEY_PREFIX}{playerId}";

				// Redis Hash로 3D 좌표 저장 (메모리 효율적)
				Dictionary<string, string> positionData = ToDict(posInfo);

				// 배치 실행으로 성능 최적화
				IRedisBatch batch = _redis.CreateBatch();

				// 위치 정보 저장
				batch.HashSet( key, positionData );

				// 만료 시간 설정
				batch.KeyExpire( key, TimeSpan.FromMinutes( POSITION_EXPIRE_MINUTES ) );

				// 활성 플레이어 목록에 추가
				batch.SetAdd( ACTIVE_PLAYERS_SET, playerId.ToString() );

				// 배치 실행 - 위의 내용 일괄 적용.
				await batch.ExecuteAsync();

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
				IReadOnlyDictionary<string, string> positionData = await _redis.HashGetAllAsync(key);

				if(positionData.Count == 0)
					return null;

				return ToPosInfo(positionData);
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
				var positionTasks = new Dictionary<long, Task<IReadOnlyDictionary<string, string>>>();

				foreach(var playerIdValue in activePlayerIds)
				{
					if(long.TryParse( playerIdValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerId ))
					{
						var key = $"{POSITION_KEY_PREFIX}{playerId}";
						positionTasks[ playerId ] = batch.HashGetAllAsync( key );
					}
				}

				await batch.ExecuteAsync();

				// 3D 거리 계산 및 반경 내 플레이어 필터링
				foreach(var kvp in positionTasks)
				{
					var positionData = await kvp.Value;
					if(positionData.Count == 0) continue;

					var playerPos = ToPosInfo(positionData);

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
				batch.KeyDelete( key );
				batch.SetRemove( ACTIVE_PLAYERS_SET, playerId.ToString() );
				await batch.ExecuteAsync();

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

		private static Dictionary<string, string> ToDict( PosInfo posInfo ) => new()
		{
			[ "x" ] = posInfo.PosX.ToString( CultureInfo.InvariantCulture ),
			[ "y" ] = posInfo.PosY.ToString( CultureInfo.InvariantCulture ),
			[ "z" ] = posInfo.PosZ.ToString( CultureInfo.InvariantCulture ),
			[ "rotX" ] = posInfo.RotationX.ToString( CultureInfo.InvariantCulture ),
			[ "rotY" ] = posInfo.RotationY.ToString( CultureInfo.InvariantCulture ),
			[ "rotZ" ] = posInfo.RotationZ.ToString( CultureInfo.InvariantCulture ),
			[ "timestamp" ] = posInfo.Timestamp.ToString( CultureInfo.InvariantCulture ),
			[ "lastUpdate" ] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString( CultureInfo.InvariantCulture ),
		};

		private static PosInfo ToPosInfo( IReadOnlyDictionary<string, string> dict ) => new()
		{
			PosX = GetFloat( dict, "x" ),
			PosY = GetFloat( dict, "y" ),
			PosZ = GetFloat( dict, "z" ),
			RotationX = GetFloat( dict, "rotX" ),
			RotationY = GetFloat( dict, "rotY" ),
			RotationZ = GetFloat( dict, "rotZ" ),
			Timestamp = GetLong( dict, "timestamp" ),
		};

		private static float GetFloat( IReadOnlyDictionary<string, string> dict, string key )
			=> dict.TryGetValue( key, out var v ) && float.TryParse( v, NumberStyles.Float, CultureInfo.InvariantCulture, out var result ) ? result : 0f;

		private static long GetLong( IReadOnlyDictionary<string, string> dict, string key )
			=> dict.TryGetValue( key, out var s ) && long.TryParse( s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v ) ? v : 0L;
	}
}
