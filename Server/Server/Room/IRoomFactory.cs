using System;
using Protocol;

namespace Server.Room
{
	/// <summary>
	/// Room 생성을 담당하는 Factory 인터페이스
	/// </summary>
	public interface IRoomFactory
	{
		/// <summary>
		/// Room을 생성합니다.
		/// </summary>
		/// <param name="roomType"></param>
		/// <param name="roomName"></param>
		/// <param name="maxPlayers"></param>
		/// <param name="serviceProvider"></param>
		/// <returns></returns>
		IRoom CreateRoom(RoomType roomType, int roomId, string roomName, int maxPlayers, IServiceProvider serviceProvider);
	}
}
