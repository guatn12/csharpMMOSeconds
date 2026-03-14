using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Data.Models;
using Server.Extensions;
using Server.Game.Map;
using Server.Room;
using Server.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Game.Monsters
{
	public class MonsterAI
	{
		private readonly Monster _monster;
		private readonly IRoom _room;
		private readonly ILogger _logger;

		// AI 셋팅
		private readonly float _detectionRange;		// 플레이어 감지 범위
		private readonly float _attackRange;		// 공격 범위
		private readonly float _returnRange;        // 귀환 시작 거리 
		private readonly float _patrolRadius;       // 배회 반경

		// AI 업데이트 주기
		private DateTime _lastUpdateTime;
		private readonly TimeSpan _updateInterval = TimeSpan.FromMilliseconds(500);

		// 공격 쿨타임
		private DateTime _lastAttackTime;
		private readonly TimeSpan _attackCooldown = TimeSpan.FromSeconds(2);

		// 배회
		private PosInfo _currentPatrolTarget;
		private DateTime _lastPatrolChange;
		private readonly TimeSpan _patrolChangeInterval = TimeSpan.FromSeconds(3);

		// Idle 타임
		private DateTime _lastIdleTime;

		// 타겟 손실 유예 (플레이어를 찾지 못했을 때 바로 귀환하지 않음)
		private DateTime _targetLostTime = DateTime.MinValue;
		private readonly TimeSpan _targetLostGracePeriod = TimeSpan.FromSeconds(3);

		// A* 경로 탐색을 위한 필드
		private List<(int x, int z)> _currentPath = new();
		private int _pathIndex = 0;
		private (int x, int z) _lastTargetCell;	// 경로 재계산 판단용

		public MonsterAI( Monster monster, IRoom room, ILogger logger )
		{
			_monster = monster;
			_room = room;
			_logger = logger;

			// 몬스터 타입에 따른 AI 설정
			_detectionRange = monster.StaticData.DetectRange;

			_attackRange = monster.StaticData.AttackRange;		// 플레이어 공격 범위(5m)보다 크게 설정
			_returnRange = monster.StaticData.ReturnRange;
			_patrolRadius = monster.StaticData.PatrolRadius;

			_lastUpdateTime = DateTime.UtcNow;
			_lastAttackTime = DateTime.UtcNow;
			_lastPatrolChange = DateTime.UtcNow;
		}

		public void Update()
		{
			if(!_monster.IsAlive) return;

			// 업데이트 주기 체크
			DateTime now = DateTime.UtcNow;
			if(now - _lastUpdateTime < _updateInterval) return;

			_lastUpdateTime = now;

			// 상태별 업데이트
			switch(_monster.CreatureState)
			{
			case State.Idle:
				UpdateIdleState();
				break;
			case State.InCombat:
				UpdateAttackState();
				break;
			case State.Patrol:
				UpdatePatrolState();
				break;
			case State.Chase:
				UpdateChaseState();
				break;
			case State.Return:
				UpdateReturnState();
				break;
			}
		}

		private void UpdateIdleState()
		{
			// 주변 플레이어 탐색
			Player nearestPlayer = FindNearestPlayer();
			if(nearestPlayer != null)
			{
				float distance = Position3DValidator.CalculateDistance3D(_monster.PosInfo, nearestPlayer.PosInfo);
				if(distance <= _detectionRange)
				{
					_monster.SetTarget( nearestPlayer.ObjectId );
					ClearPath();
					_monster.SetState( State.Chase );
					_logger.LogDebug( "Monster {MonsterId} detected player {PlayerId}", _monster.ObjectId,
						nearestPlayer.ObjectId );
					return;
				}
			}

			// 일정 시간 후 배회
			if(TimeSpan.FromSeconds( 2 ) < DateTime.UtcNow - _lastIdleTime)
			{
				ClearPath();
				_monster.SetState( State.Patrol );
			}
		}

		private void UpdatePatrolState()
		{
			// 주변 플레이어 탐색
			Player nearestPlayer = FindNearestPlayer();
			if(nearestPlayer != null)
			{
				float distance = Position3DValidator.CalculateDistance3D(_monster.PosInfo, nearestPlayer.PosInfo);
				if(distance <= _detectionRange)
				{
					_monster.SetTarget( nearestPlayer.ObjectId );
					ClearPath();
					_monster.SetState( State.Chase );
					_logger.LogDebug( "Monster {MonsterId} detected player {PlayerId}", _monster.ObjectId,
						nearestPlayer.ObjectId );
					return;
				}
			}

			// 배회 위치 변경
			if(_currentPatrolTarget == null || _patrolChangeInterval < DateTime.UtcNow - _lastPatrolChange)
			{
				_currentPatrolTarget = GetRandomPatrolPosition();
				_lastPatrolChange = DateTime.UtcNow;
			}

			// 배회 위치로 이동
			if(!MoveAlongPath(_currentPatrolTarget, _monster.StaticData.MoveSpeed * 0.5f))
			{
				// 경로 없으면 새 배회 위치 선택
				_currentPatrolTarget = GetRandomPatrolPosition();
				_lastPatrolChange = DateTime.UtcNow;
			}

			// 목표 위치 도달 확인
			float distanceToTarget = Position3DValidator.CalculateDistance3D(_monster.PosInfo, _currentPatrolTarget);
			if(distanceToTarget < 0.5f)
			{
				ClearPath();
				_monster.SetState( State.Idle );
				_lastIdleTime = DateTime.UtcNow;
			}
		}

		public void UpdateAttackState()
		{
			// 타겟 플레이어 찾기
			var targetPlayer = _room.Players.FirstOrDefault(p => p.Player.ObjectId == _monster.TargetPlayerId);
			if(targetPlayer == null || !targetPlayer.Player.IsAlive)
			{
				// 타겟을 처음 잃어버린 경우 시간 기록
				if(_targetLostTime == DateTime.MinValue)
				{
					_targetLostTime = DateTime.UtcNow;
					return;
				}

				// 유예 기간이 지났으면 귀환
				if(_targetLostGracePeriod <= DateTime.UtcNow - _targetLostTime)
				{
					_targetLostTime = DateTime.MinValue;
					_monster.ClearTarget();
					_monster.SetState( State.Return );
					ClearPath();
				}
				return;
			}

			// 타겟을 찾았으면 유예 시간 초기화
			_targetLostTime = DateTime.MinValue;

			float distanceToTarget = Position3DValidator.CalculateDistance3D(_monster.PosInfo, targetPlayer.Player.PosInfo);

			// 공격 범위 이탈
			if(_attackRange < distanceToTarget)
			{
				ClearPath();
				_monster.SetState( State.Chase );
				return;
			}

			// 스폰 위치
			float distanceToSpawn = _monster.GetDistanceToSpawn();
			if(_returnRange < distanceToSpawn)
			{
				ClearPath();
				_monster.ClearTarget();
				_monster.SetState( State.Return );
				return;
			}

			// 공격 쿨타임 체크
			DateTime now = DateTime.UtcNow;
			if(_attackCooldown <= now - _lastAttackTime)
			{
				PerformAttack( targetPlayer );
				_lastAttackTime = now;
			}
		}

		public void UpdateReturnState()
		{
			float distanceToSpawn = _monster.GetDistanceToSpawn();
			if(distanceToSpawn < 0.5f)
			{
				_monster.UpdatePosition( _monster.SpawnPosition );
				_monster.Restore();
				_monster.SetState( State.Idle );
				ClearPath();
				_lastIdleTime = DateTime.UtcNow;
				_logger.LogDebug( "Monster {MonsterId} retuned to spawn position", _monster.ObjectId );
				return;
			}

			if(!MoveAlongPath(_monster.SpawnPosition, _monster.StaticData.MoveSpeed * 1.5f))
			{
				MoveTowards( _monster.SpawnPosition, _monster.StaticData.MoveSpeed *1.5f );
			}
		}

		public void UpdateChaseState()
		{
			// 타겟 플레이어 찾기
			var targetPlayer = _room.Players.FirstOrDefault(p => p.Player.ObjectId == _monster.TargetPlayerId);
			if(targetPlayer == null || !targetPlayer.Player.IsAlive)
			{
				// 타겟을 처음 잃어버린 경우 시간 기록
				if(_targetLostTime == DateTime.MinValue)
				{
					_targetLostTime = DateTime.UtcNow;
					return;
				}

				// 유예 기간이 지났으면 귀환
				if(_targetLostGracePeriod <= DateTime.UtcNow - _targetLostTime)
				{
					_targetLostTime = DateTime.MinValue;
					_monster.ClearTarget();
					_monster.SetState( State.Return );
					ClearPath();
				}
				return;
			}

			// 타겟을 찾았으면 유예 시간 초기화
			_targetLostTime = DateTime.MinValue;

			float distanceToTarget = Position3DValidator.CalculateDistance3D(_monster.PosInfo, targetPlayer.Player.PosInfo);

			// 공격 범위 진입
			if(distanceToTarget <= _attackRange)
			{
				_monster.SetState( State.InCombat );
				ClearPath();
				return;
			}

			// 스폰 위치에서 너무 멀어지면 귀환
			float distanceToSpawn = _monster.GetDistanceToSpawn();
			if(_returnRange < distanceToSpawn)
			{
				_monster.ClearTarget();
				_monster.SetState( State.Return );
				ClearPath();
				_logger.LogDebug( "Monster {MonsterId} too far from spawn, returning", _monster.ObjectId );
				return;
			}

			// 이동
			if(!MoveAlongPath(targetPlayer.Player.PosInfo, _monster.StaticData.MoveSpeed))
			{
				// 경로를 찾지 못함 - 직선 이동 시도(풀백)
				MoveTowards( targetPlayer.Player.PosInfo, _monster.StaticData.MoveSpeed );
			}
			
		}

		// 공격 수행
		private void PerformAttack( IClientSession targetPlayer )
		{
			// 데미지
			int damage = CalculateDamage(targetPlayer);

			// 플레이어에게 데미지 적용
			bool damaged = targetPlayer.Player.TakeDamage( damage, 0 );
			if( damaged )
			{
				_logger.LogInformation( "Monster {MonsterId} attacked Player {PlayerId} for {Damage} damage",
					_monster.ObjectId, targetPlayer.Player.ObjectId, damage);

				// TODO: S_Damage 패킷 브로드캐스트
				S_Damage damagePacket = new S_Damage
				{
					Attacker = _monster.ToObjectDamageInfo(damage, false),
					Targets = { targetPlayer.Player.ToObjectDamageInfo(damage, false) }
				};

				_room.BroadcastInRange( damagePacket, _monster.PosInfo );
			}
		}

		// 데미지 계산
		private int CalculateDamage( IClientSession targetPlayer )
		{
			int baseDamage = _monster.StaticData.Attack;
			int defense = targetPlayer.Player.GetTotalDefense();

			// 기본 데미지 계산: 공 - 방(최소 1)
			int damage = Math.Max(1, baseDamage - defense);

			return damage;
		}

		public Player FindNearestPlayer()
		{
			Player nearestPlayer = null;
			float minDistance = float.MaxValue;

			var Players = _room.RoomMap.GetNearByPlayers(_monster.PosInfo.PosX, _monster.PosInfo.PosZ, (int)_detectionRange);

			foreach(var player in Players)
			{
				if(!player.IsAlive) continue;

				float distance = Position3DValidator.CalculateDistance3D(_monster.PosInfo, player.PosInfo);
				if( distance < minDistance )
				{
					minDistance = distance;
					nearestPlayer = player;
				}
			}

			return nearestPlayer;
		}

		// 랜덤 배회 위치 생성
		public PosInfo GetRandomPatrolPosition()
		{
			Random random = Random.Shared;
			int maxAttempts = 10;

			for(int i = 0; i < maxAttempts; i++)
			{
				// 스폰 위치 기준으로 _patrolRadius 반경 내 랜덤 위치
				float angle = (float)(random.NextDouble() * 2 * Math.PI);
				float distance = (float)(random.NextDouble() * _patrolRadius);

				float posX = _monster.SpawnPosition.PosX + distance * (float)Math.Cos(angle);
				float posZ = _monster.SpawnPosition.PosZ + distance * (float)Math.Sin(angle);

				if(_room.RoomMap.IsWalkableWorld(posX, posZ))
				{
					return new PosInfo
					{
						PosX = posX,
						PosY = _room.RoomMap.MapData.GetWorldHeight( posX, posZ ),
						PosZ = posZ,
					};
				}
			}

			// 전부 실패 시 현재 위치 유지
			return new PosInfo
			{
				PosX = _monster.PosInfo.PosX,
				PosY = _monster.PosInfo.PosY,
				PosZ = _monster.PosInfo.PosZ,
			};
		}

		public void MoveTowards( PosInfo target, float speed )
		{
			if(target == null ) return;

			PosInfo current = _monster.PosInfo;

			//방향 벡터 계산
			float dx = target.PosX - current.PosX;
			float dy = target.PosY - current.PosY;
			float dz = target.PosZ - current.PosZ;

			float distance = (float)Math.Sqrt(dx*dx+dy*dy+dz*dz);

			if(distance < 0.01f) return;

			// 정규화된 방향 벡터
			float dirX = dx / distance;
			float dirY = dy / distance;
			float dirZ = dz / distance;

			// 이동 거리 (초당 speed 단위)
			float moveDistance = speed * (float)_updateInterval.TotalSeconds;

			// 목표 위치보다 가까우면 목표 위치로 이동.
			if( distance <= moveDistance)
			{
				if(!_room.RoomMap.IsWalkableWorld(target.PosX, target.PosZ))
				{
					_logger.LogWarning( "Monster {MonsterId} Move Position Is Not Walkable. newPosition: {X}, {Z}",
						_monster.ObjectId, target.PosX, target.PosZ );
					return;
				}

				_monster.UpdatePosition( target );
				_room.RoomMap.UpdateMonsters( _monster, target.PosX, target.PosZ );
			}
			else
			{
				float posX = current.PosX + dirX * moveDistance;
				float posY = current.PosY + dirY * moveDistance;
				float posZ = current.PosZ + dirZ * moveDistance;

				if(!_room.RoomMap.IsWalkableWorld(posX, posZ))
				{
					_logger.LogWarning( "Monster {MonsterId} Move Position Is Not Walkable. newPosition: {X}, {Z}",
						_monster.ObjectId, posX, posZ );
					return;
				}

				PosInfo newPosition = new PosInfo
				{
					PosX = posX,
					PosY = posY,
					PosZ = posZ,
					RotationX = current.RotationX,
					RotationY = (float)Math.Atan2(dirX, dirZ) * (180f / (float)Math.PI), // Yaw 계산
					RotationZ = current.RotationZ
				};

				_monster.UpdatePosition( newPosition );
				_room.RoomMap.UpdateMonsters( _monster, newPosition.PosX, newPosition.PosZ );
			}

			// TODO: S_Move 패킷 브로드캐스트
			S_Move movePacket = new S_Move
			{
				Objects = { _monster.ToObjectInfo() }
			};
			_room.BroadcastInRange( movePacket, _monster.PosInfo );
		}

		/// <summary>
		/// A* 경로를 따라 다음 웨이포인트로 이동합니다.
		/// </summary>
		/// <param name="targetPos">최종 목표 위치 (경로 재계산 판단용)</param>
		/// <param name="speed">이동 속도</param>
		/// <returns>true: 이동 중, false: 경로 완료 또는 실패</returns>
		private bool MoveAlongPath(PosInfo targetPos, float speed)
		{
			var startCell = _room.RoomMap.WorldToCell(_monster.PosInfo.PosX, _monster.PosInfo.PosZ);
			var goalCell = _room.RoomMap.WorldToCell(targetPos.PosX, targetPos.PosZ);

			// 맨해튼 거리 3셀 이내 + 직선 경로에 장애물 없음 -> 직선 이동
			int manhattan = Math.Abs(startCell.x - goalCell.x) + Math.Abs(startCell.z - goalCell.z);
			if(manhattan <= 3)
			{
				MoveTowards( targetPos, speed );
				return true;
			}

			// 경로가 없거나 완료됨 -> 재계산 필요.
			if(_currentPath.Count == 0 ||  _currentPath.Count <= _pathIndex)
			{
				if(!RecalculatePath( targetPos ))
					return false;
			}

			// 타겟 위치 변화 감지 - 재계산
			var targetCell = _room.RoomMap.WorldToCell(targetPos.PosX, targetPos.PosZ);
			int dx = Math.Abs(targetCell.x - _lastTargetCell.x);
			int dz = Math.Abs(targetCell.z - _lastTargetCell.z);
			if(3 <= dx + dz)		// 3Cell 이상 이동하면 경로 재계산
			{
				if(!RecalculatePath( targetPos ))
					return false;
			}

			// 현재 웨이포인트로 이동
			var (wpX, wpZ) = _currentPath[ _pathIndex];
			var (worldX, worldZ) = _room.RoomMap.CellToWorld(wpX, wpZ);
			float height = _room.RoomMap.MapData.GetHeightAt(wpX, wpZ);

			var waypointPos = new PosInfo
			{
				PosX = worldX,
				PosY = height,
				PosZ = worldZ
			};

			MoveTowards( waypointPos, speed );

			// 웨이포인트 도달 확인
			float dist = Position3DValidator.CalculateDistance3D(_monster.PosInfo, waypointPos);
			if(dist < 0.5f)
			{
				_pathIndex++;
			}

			return true;
		}

		/// <summary>
		/// A* 경로 재계산
		/// </summary>
		/// <param name="targetPos">타겟 위치</param>
		/// <returns></returns>
		private bool RecalculatePath(PosInfo targetPos)
		{
			var startCell = _room.RoomMap.WorldToCell(_monster.PosInfo.PosX, _monster.PosInfo.PosZ);
			var goalCell = _room.RoomMap.WorldToCell(targetPos.PosX, targetPos.PosZ);

			_currentPath = PathFinder.FindPath( _room.RoomMap.MapData, startCell.x, startCell.z, goalCell.x, goalCell.z );

			_pathIndex = 0;
			_lastTargetCell = goalCell;

			return 0 < _currentPath.Count;
		}

		/// <summary>
		/// 현재 경로 초기화
		/// </summary>
		private void ClearPath()
		{
			_currentPath.Clear();
			_pathIndex = 0;
		}
	}
}
