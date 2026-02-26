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

			if(objectInfo.Type == null || objectInfo.Type == ObjectType.ObjectNone || objectInfo.Type != ObjectType.ObjectPlayer)
				return null;

			return new ClientPlayerInfo
			{
				PlayerId = objectInfo.ObjectId,
				PlayerName = string.IsNullOrEmpty( objectInfo.Name ) ? $"Player_{objectInfo.ObjectId}" : objectInfo.Name,

				Position = objectInfo.PosInfo?.Clone() ?? new PosInfo(),
				Stats = objectInfo.StatInfo.Clone(),
				
			};
		}
	}
}
