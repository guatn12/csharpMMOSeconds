using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
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
		private readonly float _detectionRange;     // 플레이어 감지 범위
		private readonly float _attackRange;        // 공격 범위
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

		// 타겟 손실 유예 (플레이어를 찾지 못했을 때 바로 귀환하지 않음)
		private DateTime _targetLostTime = DateTime.MinValue;
		private readonly TimeSpan _targetLostGracePeriod = TimeSpan.FromSeconds(3);

		public MonsterAI( Monster monster, IRoom room, ILogger logger )
		{
			_monster = monster;
			_room = room;
			_logger = logger;

			// 몬스터 타입에 따른 AI 설정
			_detectionRange = monster.StaticData.MonsterType switch
			{
				"Normal" => 10.0f,
				"Elite" => 15.0f,
				"Boss" => 20.0f,
				_ => 10.0f
			};

			_attackRange = 6.0f;		// 플레이어 공격 범위(5m)보다 크게 설정
			_returnRange = 25.0f;
			_patrolRadius = 5.0f;

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
			switch(_monster.State)
			{
			case MonsterState.MonsterIdle:
				UpdateIdleState();
				break;
			case MonsterState.MonsterAttack:
				UpdateAttackState();
				break;
			case MonsterState.MonsterPatrol:
				UpdatePatrolState();
				break;
			case MonsterState.MonsterChase:
				UpdateChaseState();
				break;
			case MonsterState.MonsterReturn:
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
				float distance = Position3DValidator.CalculateDistance3D(_monster.Position, nearestPlayer.Position);
				if(distance <= _detectionRange)
				{
					_monster.SetTarget( nearestPlayer.PlayerId );
					_monster.UpdateState( MonsterState.MonsterChase );
					_logger.LogDebug( "Monster {MonsterId} detected player {PlayerId}", _monster.MonsterId,
						nearestPlayer.PlayerId );
					return;
				}
			}

			// 일정 시간 후 배회
			if(TimeSpan.FromSeconds( 2 ) < DateTime.UtcNow - _lastUpdateTime)
			{
				_monster.UpdateState( MonsterState.MonsterPatrol );
			}
		}

		private void UpdatePatrolState()
		{
			// 주변 플레이어 탐색
			Player nearestPlayer = FindNearestPlayer();
			if(nearestPlayer != null)
			{
				float distance = Position3DValidator.CalculateDistance3D(_monster.Position, nearestPlayer.Position);
				if(distance <= _detectionRange)
				{
					_monster.SetTarget( nearestPlayer.PlayerId );
					_monster.UpdateState( MonsterState.MonsterChase );
					_logger.LogDebug( "Monster {MonsterId} detected player {PlayerId}", _monster.MonsterId,
						nearestPlayer.PlayerId );
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
			MoveTowards( _currentPatrolTarget, _monster.StaticData.MoveSpeed * 0.5f );

			// 목표 위치 도달 확인
			float distanceToTarget = Position3DValidator.CalculateDistance3D(_monster.Position, _currentPatrolTarget);
			if(distanceToTarget < 0.5f)
			{
				_monster.UpdateState( MonsterState.MonsterIdle );
			}
		}

		public void UpdateAttackState()
		{
			// 타겟 플레이어 찾기
			var targetPlayer = _room.Players.FirstOrDefault(p => p.Player.PlayerId == _monster.TargetPlayerId);
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
					_monster.UpdateState( MonsterState.MonsterReturn );
				}
				return;
			}

			// 타겟을 찾았으면 유예 시간 초기화
			_targetLostTime = DateTime.MinValue;

			float distanceToTarget = Position3DValidator.CalculateDistance3D(_monster.Position, targetPlayer.Player.Position);

			// 공격 범위 이탈
			if(_attackRange < distanceToTarget)
			{
				_monster.UpdateState( MonsterState.MonsterChase );
				return;
			}

			// 스폰 위치
			float distanceToSpawn = _monster.GetDistanceToSpawn();
			if(_returnRange < distanceToSpawn)
			{
				_monster.ClearTarget();
				_monster.UpdateState( MonsterState.MonsterReturn );
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
				_monster.UpdateState( MonsterState.MonsterIdle );
				_logger.LogDebug( "Monster {MonsterId} retuned to spawn position", _monster.MonsterId );
				return;
			}

			MoveTowards( _monster.SpawnPosition, _monster.StaticData.MoveSpeed *1.5f );
		}

		public void UpdateChaseState()
		{
			// 타겟 플레이어 찾기
			var targetPlayer = _room.Players.FirstOrDefault(p => p.Player.PlayerId == _monster.TargetPlayerId);
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
					_monster.UpdateState( MonsterState.MonsterReturn );
				}
				return;
			}

			// 타겟을 찾았으면 유예 시간 초기화
			_targetLostTime = DateTime.MinValue;

			float distanceToTarget = Position3DValidator.CalculateDistance3D(_monster.Position, targetPlayer.Player.Position);

			// 공격 범위 진입
			if(distanceToTarget <= _attackRange)
			{
				_monster.UpdateState( MonsterState.MonsterAttack );
				return;
			}

			// 스폰 위치에서 너무 멀어지면 귀환
			float distanceToSpawn = _monster.GetDistanceToSpawn();
			if(_returnRange < distanceToSpawn)
			{
				_monster.ClearTarget();
				_monster.UpdateState( MonsterState.MonsterReturn );
				_logger.LogDebug( "Monster {MonsterId} too far from spawn, returning", _monster.MonsterId );
				return;
			}

			// 이동
			MoveTowards( targetPlayer.Player.Position, _monster.StaticData.MoveSpeed );
		}

		// 공격 수행
		private void PerformAttack( IClientSession targetPlayer )
		{
			// 데미지
			int damage = CalculateDamage(targetPlayer);

			// 플레이어에게 데미지 적용
			bool damaged = targetPlayer.Player.TakeDamage( damage );
			if( damaged )
			{
				_logger.LogInformation( "Monster {MonsterId} attacked Player {PlayerId} for {Damage} damage",
					_monster.MonsterId, targetPlayer.Player.PlayerId, damage);

				// TODO: S_MonsterAttack 패킷 브로드캐스트(BaseRoom에서 처리)
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

			foreach(var session in _room.Players)
			{
				if(!session.Player.IsAlive) continue;

				float distance = Position3DValidator.CalculateDistance3D(_monster.Position, session.Player.Position);
				if( distance < minDistance )
				{
					minDistance = distance;
					nearestPlayer = session.Player;
				}
			}

			return nearestPlayer;
		}

		// 랜덤 배회 위치 생성
		public PosInfo GetRandomPatrolPosition()
		{
			Random random = new Random();

			// 스폰 위치 기준으로 _patrolRadius 반경 내 랜덤 위치
			float angle = (float)(random.NextDouble() * 2 * Math.PI);
			float distance = (float)(random.NextDouble() * _patrolRadius);

			float offsetX = distance * (float)Math.Cos(angle);
			float offsetZ = distance * (float)Math.Sin(angle);

			return new PosInfo
			{
				PosX = _monster.SpawnPosition.PosX + offsetX,
				PosY = _monster.SpawnPosition.PosY,
				PosZ = _monster.SpawnPosition.PosZ + offsetZ,
			};
		}

		public void MoveTowards( PosInfo target, float speed )
		{
			if(target == null ) return;

			PosInfo current = _monster.Position;

			//방향 벡터 계산
			float dx = target.PosX - current.PosX;
			float dy = target.PosY - current.PosY;
			float dz = target.PosZ - current.PosZ;

			float distance = (float)Math.Sqrt(dx*dx+dy*dy+dx*dx);

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
				_monster.UpdatePosition( target );
			}
			else
			{
				PosInfo newPosition = new PosInfo
				{
					PosX = current.PosX + dirX * moveDistance,
					PosY = current.PosY + dirY * moveDistance,
					PosZ = current.PosZ + dirZ * moveDistance,
					RotationX = current.RotationX,
					RotationY = (float)Math.Atan2(dirX, dirZ) * (180f / (float)Math.PI), // Yaw 계산
					RotationZ = current.RotationZ
				};

				_monster.UpdatePosition( newPosition );
			}
		}
	}
}
