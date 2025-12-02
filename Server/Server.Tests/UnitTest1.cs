using Protocol;
using Server.Game;

namespace Server.Tests
{
	public class UnitTest1
	{
		[Fact]
		public void Test1()
		{
			var player = new Player(1, "TestPlayer");

			Assert.NotNull( player );
			Assert.Equal( "TestPlayer", player.Info.Name );
		}
	}
}