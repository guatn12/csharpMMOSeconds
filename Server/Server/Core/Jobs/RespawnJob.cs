using Microsoft.Extensions.Logging;
using Protocol;
using Server.Core.Session;
using Server.Room;
using ServerCore;
using System;

namespace Server.Core.Jobs
{
	public class RespawnJob : IJob
	{
		private ILogger _logger;
		private IRoom _room;
		private IClientSession _session;

		/// <summary>
		/// Job 실행에 필요한 컨텍스트 초기화
		/// </summary>
		public void Initialize( IRoom room, IClientSession session, ILogger logger )
		{
			_room = room;
			_session = session;
			_logger = logger;
		}

		public void Clear()
		{
			_room = null;
			_session = null;
			_logger = null;
		}

		public void Execute()
		{
			try
			{
				// 플레이어 부활 처리
				_session.Player.Revive();
				
				// 룸에 플레이어 재입장 시도
				var result = _room.TryEnterAsync( _session ).GetAwaiter().GetResult();
				if(RoomEnterResult.Success != result )
				{
					_logger.LogWarning( "RespawnJob Execute Failed to Re-Enter Room for PlayerId: {PlayerId}", _session.PlayerId );
					return;
				}

				// 부활 및 재입장 알림
				_session.CurrentRoom.SendToPlayer( _session, new S_EnterGame()
				{
					Player = _session.Player.ToObjectInfo(),
					MapId = _session.CurrentRoom.RoomMap.MapId,
				} );
			}
			catch(Exception ex )
			{
				_logger.LogError( ex, "RespawnJob Execute Error for PlayerId: {PlayerId}", _session.PlayerId );
			}
		}
	}
}
