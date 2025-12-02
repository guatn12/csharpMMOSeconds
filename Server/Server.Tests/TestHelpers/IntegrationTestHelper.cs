using Castle.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Server.Config;
using Server.Data;
using Server.Room;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.TestHelpers
{
	/// <summary>
	/// 통합 테스트용 헬퍼 클래스
	/// 실제 LobbyRoom을 생성하여 테스트에 사용
	/// </summary>
	public static class IntegrationTestHelper
	{
		/// <summary>
		/// 테스트용 LobbyRoom 생성
		/// 모든 의존성을 Mock으로 주입하여 최소한의 실제 동작만 테스트
		/// </summary>
		/// <param name="roomName"></param>
		/// <param name="maxPlayers"></param>
		/// <returns></returns>
		public static LobbyRoom CreateTestLobbyRoom(
			string roomName = "TestLobby",
			int maxPlayers = 10)
		{
			// 1. Logger Mock
			var mockLogger = new Mock<ILogger<LobbyRoom>>();

			// 2. ServerSettings Mock
			var serverSettings = new ServerSettings
			{
				Room = new RoomConfig
				{
					Lobby = new LobbyConfig
					{
						MaxPlayers = maxPlayers,
						DefaultName = "Test Lobby"
					}
				}
			};

			// IOptions<ServerSettings>로 감싸기
			var mockOptions = Options.Create(serverSettings);

			// 3. DataManager Mock
			// DataManager는 GameDataConfig를 필요로 함
			var gameDataConfig = new GameDataConfig
			{
				DataPath = "GameData"	// 테스트용 경로
			};

			var mockDataManagerOptions = Options.Create(gameDataConfig);
			var mockDataManager = new Mock<DataManager>(mockDataManagerOptions);
		}
	}
}
