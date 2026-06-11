using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Protocol;
using Server.Core.Session;
using Server.Packet;
using Server.Room;
using Server.Tests.TestHelpers;
using ServerCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Session
{
	public class SessionQueueIntegrationTests
	{
		// 공통
		private static (PacketManager pm, ClientSession session, JobQueueManager jq, Mock<IRoomManager> roomManager) Build(bool connected = true)
		{
			var jq = new JobQueueManager(NullLogger<JobQueueManager>.Instance);
			var roomManager = new Mock<IRoomManager>();
			var systemHandler = MockFactoryHelper.CreateSystemPacketHandler(roomManager, new Mock<IRoomTransitionCoordinator>());
			var (pm, _) = MockFactoryHelper.CreateRealPacketManager( jq, systemHandler );
			var session = MockFactoryHelper.CreateRealClientSessionWithQueue(pm,jq, sessionId:1, connected: connected);
			return (pm, session, jq, roomManager);
		}

		// 검증 3: 동일 세션 SYSTEM 잡의 세션 큐 직렬성
		[Fact]
		public async Task SystemPacket_SerializedInSessionQueue()
		{
			static IJob MakeJob(JobQueueManager jq, Func<Task> body)
			{
				var job = jq.JobPool.Get<DelegateJob>();
				job.Initialize( body );
				return job;
			}

			var (_, session, jq, _) = Build( connected: true );
			jq.Start( workerCount: 2 );				// 워커 2개로도 한 세션 큐는 직렬이어야 한다.
			try
			{
				int concurrent = 0, maxObserved = 0;
				var done = new CountdownEvent(2);

				Func<Task> body = async () =>								// DelegateJob.Initialize는 Func<Task>
				{
					int now = Interlocked.Increment(ref concurrent);
					int prev;
					do
					{
						prev = Volatile.Read(ref maxObserved);
						if(now <= prev) break;
					}
					while(Interlocked.CompareExchange(ref maxObserved, now, prev) != prev);

					await Task.Delay(30);                       // 겹칠 틈을 의도적으로 연다(이게 없으면 거짓 통과)
					Interlocked.Decrement(ref concurrent);
					done.Signal();
				};

				session.EnqueueSystemJob( MakeJob( jq, body ) );
				session.EnqueueSystemJob( MakeJob( jq, body ) );

				Assert.True( done.Wait( 2000 ) );
				Assert.Equal( 1, maxObserved );                   // 직렬이면 동시 실행 최대치는 1
			}
			finally
			{
				await jq.StopAsync();
			}
		}

		// 검증 4+5: System throw가 JobSerializer 중앙 catch로 흡수, 워커 생존
		[Fact]
		public async Task SystemPacketHandler_Throw_IsolatedByJobSerializer()
		{
			var (pm, session, jq, roomManager) = Build(connected: true);
			// 핸들러가 throw 하도록 - TM-1 임시 try/catch가 제거된 상태에서도 격리되야 함
			roomManager.Setup( m => m.JoinDefaultLobbyAsync( It.IsAny<IClientSession>() ) )
				.ThrowsAsync( new InvalidOperationException( "test" ) );

			jq.Start( workerCount: 1 );

			try
			{
				// 1. throw 하는 SYSTEM job
				var throwing = pm.RouteIncoming(session, MockFactoryHelper.SerializeClientPacket(PacketID.C_EnterGame, new C_EnterGame()));
				session.EnqueueSystemJob( throwing.Job );

				// 2. 생존 증거 - 같은 큐에 신호 잡을 넣어 실행되는지 확인
				var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var signal = jq.JobPool.Get<DelegateJob>();
				signal.Initialize( () => { ran.TrySetResult(); } ); // Action 오버로드
				session.EnqueueSystemJob( signal );

				var completed = await Task.WhenAny(ran.Task, Task.Delay(2000));
				Assert.Same( ran.Task, completed );		// throw가 흡수되고 다음 잡이 정상 실행됨.
			}
			finally
			{
				await jq.StopAsync();
			}
		}

		// 검증 6 - OnRecvPacket -> 세션 큐 -> 워커 -> 핸들러 end to end
		[Fact]
		public async Task OnRecvPacket_SystemPacket_DrivenThroughSessionQueue_ExecuteHandler()
		{
			// arrange
			var (pm, session, jq, roomMgr) = Build( connected: true );
			int joinCalls = 0;
			roomMgr.Setup( m => m.JoinDefaultLobbyAsync( It.IsAny<IClientSession>() ) )
				.Returns<IClientSession>( s =>
				{
					Interlocked.Increment( ref joinCalls );
					s.SetCurrentRoom( MockFactoryHelper.CreateMockRoom( RoomType.Lobby, roomId: 1, mapId: 1 ).Object );
					return Task.FromResult( RoomEnterResult.Success );
				} );

			jq.Start( workerCount: 1 );
			
			try
			{
				// act
				var buffer = MockFactoryHelper.SerializeClientPacket(PacketID.C_EnterGame, new C_EnterGame());
				session.OnRecvPacket( buffer );                     // end to end

				await MockFactoryHelper.WaitForStateAsync( session, SessionState.InRoom );
				
				// assert
				Assert.Equal( 1, joinCalls );
			}
			finally
			{
				await jq.StopAsync();
			}
		}
	}
}
