namespace DummyClient.Data.Models
{
	public class GameConfigData
	{
		public int ViewDistance { get; set; } = 50;
		public int BroadCastRange { get; set; } = 100;
		public int PlayerDefaultMoveSpeed { get; set; } = 5;

		public bool IsValid()
		{
			return ViewDistance > 0 &&
				   BroadCastRange > 0 &&
				   PlayerDefaultMoveSpeed > 0;
		}
	}
}
