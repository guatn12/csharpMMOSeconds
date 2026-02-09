using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DummyClient.Extensions
{
	public static class DataExtensions
	{
		/// <summary>
		/// ObjectInfo(Player)를 ClientPlayerInfo로 변환
		/// </summary>
		public static ClientPlayerInfo ToClientPlayerInfo( this ObjectInfo objectInfo )
		{
			if(objectInfo == null)
				throw new ArgumentNullException( nameof( objectInfo ) );

			if(objectInfo.Type != ObjectType.ObjectPlayer || objectInfo.InfoCase != ObjectInfo.InfoOneofCase.PlayerInfo)
				throw new InvalidOperationException( $"ObjectInfo is not a player. Type:{objectInfo.Type}, InfoCase: { objectInfo.InfoCase}");

			PlayerInfo playerInfo = objectInfo.PlayerInfo;

			return new ClientPlayerInfo
			{
				PlayerId = playerInfo.PlayerId,
				PlayerName = string.IsNullOrEmpty( playerInfo.Name ) ? $"Player_{playerInfo.PlayerId}" : playerInfo.Name,
				Level = playerInfo.Level,
				CurrentExp = playerInfo.Experience,
				Position = playerInfo.PosInfo?.Clone() ?? new PosInfo(),
				Stats = new PlayerStats
				{
					MaxHP = playerInfo.MaxHP,
					MaxMP = playerInfo.MaxMP,
					CurrentHP = playerInfo.CurrentHP,
					CurrentMP = playerInfo.CurrentMP,
					// Attack, Defense는 PlayerInfo에 없음 → 기본값 유지
				}
			};
		}
	}
}
