using Microsoft.Extensions.Logging;
using Moq;
using Server.Core.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Session
{
	public class SessionStateTests
	{
		private ClientSession CreateSession() => new ClientSession(
			new Mock<ILogger<ClientSession>>().Object, null, new Mock<ISessionManager>().Object, 1 );

		[Fact]
		public void InitialState_ShouldBeConnected()
		{
			var s = CreateSession();
			Assert.Equal( SessionState.Connected, s.State );
		}

		[Theory]
		[InlineData(SessionState.Connected,				SessionState.EnteringGame,	true)]
		[InlineData(SessionState.Connected, 			SessionState.InRoom,		false)]
		[InlineData(SessionState.EnteringGame,			SessionState.InRoom,		true)]
		[InlineData(SessionState.EnteringGame,			SessionState.Connected,		true)]
		[InlineData(SessionState.InRoom,				SessionState.Transferring,	true)]
		[InlineData(SessionState.InRoom,				SessionState.Connected,		false)]
		[InlineData(SessionState.Disconnected,			SessionState.Connected,		false)]
		public void TryTransitionTo_ValidatesTransitionTable(SessionState from, SessionState to, bool expected)
		{
			var s = CreateSession();
			s.ForceState( from );// 테스트 전용 setter(internal)

			bool result = s.TryTransitionTo( to );
			Assert.Equal( expected, result ); 
		}

		[Fact]
		public async Task TryTransitionTo_UnderRace_OnlyOneSucceeds()
		{
			var s = CreateSession();
			s.ForceState( SessionState.Connected );

			var barrier = new Barrier(2);
			var results = new bool[2];

			var t1 = Task.Run( () => {barrier.SignalAndWait(); results[0] = s.TryTransitionTo(SessionState.EnteringGame); });
			var t2 = Task.Run( () => {barrier.SignalAndWait(); results[1] = s.TryTransitionTo(SessionState.EnteringGame); });

			await Task.WhenAll(t1, t2);

			Assert.True(results[0] ^ results[1]); // 정확히 하나만 성공
			Assert.Equal( SessionState.EnteringGame, s.State );
		}

		[Fact]
		public void TryTransitionTo_FromDisconnected_AllRejected()
		{
			var s = CreateSession();
			s.ForceState( SessionState.Disconnected );

			foreach(SessionState target in Enum.GetValues<SessionState>())
			{
				Assert.False( s.TryTransitionTo( target ) );
			}
		}
	}
}
