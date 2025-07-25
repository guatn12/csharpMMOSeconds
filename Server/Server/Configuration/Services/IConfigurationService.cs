using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Configuration.Services
{
	internal interface IConfigurationService
	{
		// 설정 조회
		ServerConfiguration Current { get; }
		NetworkSettings NetworkSettings { get; }
		LoggingSettings LoggingSettings { get; }
		SecuritySettings SecuritySettings { get; }
		DatabaseSettings DatabaseSettings { get; }
		JobQueueSettings JobQueueSettings { get; }

		// 설정 변경 감지
		event EventHandler<ConfigurationChangeEventArgs> ConfigurationChanged;

		// 설정 검증
		bool ValidateConfiguration(out List<string> errors);
		bool ValidateNetworkSettings(out List<string> errors);
		bool ValidateLoggingSettings(out List<string> errors);
		bool ValidateDatabaseSettings(out List<string> errors);
		bool ValidateJobQueueSettings(out List<string> errors);
		bool ValidateSecuritySettings(out List<string> errors);

		// 설정 초기화 및 관리
		Task InitializeAsync();
		Task ReloadConfigurationAsync();
		void Dispose();

		// 설정 변경 알림 등록/해제
		IDisposable RegisterChangeCallBack( Action<ServerConfiguration> callback );
		void UnregisterChangeCallBack( IDisposable registration );

		// 설정 스냅샷
		ServerConfiguration CreateSnapshot();
		bool CompareSnapshots(ServerConfiguration snapshot1, ServerConfiguration snapshot2);
	}
}
