using Moq;
using Protocol;
using Server.Room;
using Server.Tests.TestHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Tests.Infra
{
	public class TickServiceTest
	{
		private int testValue = 0;

		[Fact]
		public void TickService_AfterRegister_CallBack()
		{
			// Arrange
			var tickService = MockFactoryHelper.CreateTickService(50);
			tickService.Register( "test1", 50, testCallBack );

			// Act
			tickService.Start();
			Thread.Sleep( 300 );

			// Assert
			Assert.True( 2 < testValue );
			tickService.Stop();
		}

		[Fact]
		public void TickService_RegisterException()
		{
			//Arrange
			var tickService = MockFactoryHelper.CreateTickService();
			var testName = "test";
			var intervalMs = 40;
			var callback = testCallBack;

			// Act / Assert
			Assert.Throws<ArgumentOutOfRangeException>( () => tickService.Register(testName, intervalMs, callback ) );
		}

		[Fact]
		public void TickService_StartAfterRegister()
		{
			//Arrange
			var tickService = MockFactoryHelper.CreateTickService();

			// Act
			tickService.Register( "test1", 200, testCallBack );
			tickService.Start();

			// Act / Assert
			Assert.Throws<InvalidOperationException>( () => tickService.Register( "test2", 300, testCallBack ) );
			tickService.Stop();
		}

		[Fact]
		public void TickService_FirstExec_NotStart()
		{
			//Arrange
			var tickService = MockFactoryHelper.CreateTickService(50);
			tickService.Register( "test1", 200, testCallBack );
			testValue = 0;
			int testValue1 = 0;
			int testValue2 = 0;

			// Act
			tickService.Start();
			Thread.Sleep( 100 );
			testValue1 = testValue;
			Thread.Sleep( 200 );
			testValue2 = testValue;

			// Assert
			Assert.Equal( 0 , testValue1);
			Assert.True( 1 <= testValue2 );

			tickService.Stop();
		}

		[Fact]
		public void TickService_StopAfter_callback()
		{
			// Arrange
			var tickService = MockFactoryHelper.CreateTickService(50);
			testValue = 0;
			tickService.Register("test", 50, testCallBack );
			int testValue1 = 0;

			// Act
			tickService.Start();
			Thread.Sleep( 200 );
			tickService.Stop();
			testValue1 = testValue;
			Thread.Sleep( 200 );

			//Assert
			Assert.True( testValue <= testValue1 + 1 );
		}

		[Fact]
		public void TickService_Exception()
		{
			// Arrange
			var tickService = MockFactoryHelper.CreateTickService(50);
			testValue = 0;
			tickService.Register( "exceptionTest", 50, testCallException );
			tickService.Register( "test", 50, testCallBack );

			// Act
			tickService.Start();
			Thread.Sleep( 300 );

			//Assert
			Assert.True( 2 <= testValue );

			tickService.Stop();
		}

		[Fact]
		public void TickService_Dispose_NullSafaty()
		{
			// Arrange
			var tickService = MockFactoryHelper.CreateTickService();

			// Act / Assert
			var ex = Record.Exception(() => tickService.Dispose());
			Assert.Null( ex );
		}

		[Fact]
		public void TickService_Stop_NullSafety()
		{
			// Arrange
			var tickService = MockFactoryHelper.CreateTickService();

			// Act / Assert
			Assert.ThrowsAny<Exception>(tickService.Stop );
		}


		private void testCallBack()
		{
			Interlocked.Increment( ref testValue );
		}

		private void testCallException()
		{
			throw new Exception( "test" );
		}
	}
}
