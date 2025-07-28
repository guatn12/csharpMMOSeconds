using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Configuration.Services
{
	public class ConfigurationService : IConfigurationService, IDisposable
	{
		private readonly IOptionsMonitor<ServerConfiguration> _serverConfigMonitor;
		private readonly IOptionsMonitor<NetworkSettings> _networkMonitor;
		private readonly IOptionsMonitor<LoggingSettings> _loggingMonitor;
		private readonly IOptionsMonitor<SecuritySettings> _securityMonitor;
		private readonly IOptionsMonitor<DatabaseSettings> _databaseMonitor;
		private readonly IOptionsMonitor<JobQueueSettings> _jobQueueMonitor;
		private readonly ILogger<ConfigurationService> _logger;

		private readonly ConcurrentBag<IDisposable> _changeSubscriptions;
		private readonly ConcurrentBag<Action<ServerConfiguration>> _changeCallbacks;
		private readonly SemaphoreSlim _initializationSemaphore;

		private ServerConfiguration _currentSnapshot;
		private bool _isInitialized;
		private bool _disposed;

		public event EventHandler<ConfigurationChangeEventArgs> ConfigurationChanged;

		public ConfigurationService(
			IOptionsMonitor<ServerConfiguration> serverConfigMonitor,
			IOptionsMonitor<NetworkSettings> networkMonitor,
			IOptionsMonitor<LoggingSettings> loggingMonitor,
			IOptionsMonitor<SecuritySettings> securityMonitor,
			IOptionsMonitor<DatabaseSettings> databaseMonitor,
			IOptionsMonitor<JobQueueSettings> jobQueueMonitor,
			ILogger<ConfigurationService> logger )
		{
			_serverConfigMonitor = serverConfigMonitor;
			 _networkMonitor = networkMonitor;
			_loggingMonitor = loggingMonitor;
			_securityMonitor = securityMonitor;
			_databaseMonitor = databaseMonitor;
			_jobQueueMonitor = jobQueueMonitor;
			_logger = logger;

			_changeSubscriptions = new ConcurrentBag<IDisposable>();
			_changeCallbacks = new ConcurrentBag<Action<ServerConfiguration>>();
			_initializationSemaphore = new SemaphoreSlim( 1, 1 );
		}

		// 속성 구현
		public ServerConfiguration Current => _currentSnapshot ?? _serverConfigMonitor.CurrentValue;
		public NetworkSettings NetworkSettings => _networkMonitor.CurrentValue;
		public LoggingSettings LoggingSettings => _loggingMonitor.CurrentValue;
		public SecuritySettings SecuritySettings => _securityMonitor.CurrentValue;
		public DatabaseSettings DatabaseSettings => _databaseMonitor.CurrentValue;
		public JobQueueSettings JobQueueSettings => _jobQueueMonitor.CurrentValue;

		// 초기화
		public async Task InitializeAsync()
		{
			await _initializationSemaphore.WaitAsync();
			try
			{
				if(_isInitialized)
					return;

				_logger.LogInformation( "ConfigurationService 초기화 시작" );

				// 초기 설정 검증
				if(!ValidateConfiguration( out var errors ))
				{
					var errorMessage = string.Join(Environment.NewLine, errors);
					_logger.LogError( "설정 검증 실패:{NewLine}{Errors}", Environment.NewLine, errorMessage );
					throw new InvalidOperationException( $"설정 검증 실패: {errorMessage}" );
				}

				// 초기 스냅샷 생성
				_currentSnapshot = CreateSnapshot();

				// 변경 감지 등록
				RegisterChangeMonitors();

				_isInitialized = true;
				_logger.LogInformation( "ConfigurationService 초기화 완료" );
			}
			finally
			{
				_initializationSemaphore.Release();
			}
		}

		// 변경 감지 모니터 등록
		private void RegisterChangeMonitors()
		{
			// ServerConfiguration 변경 감지
			var serverConfigSub = _serverConfigMonitor.OnChange((config, name) =>
			{
				HandleConfigurationChange("ServerConfiguration", _currentSnapshot, config);
			});
			_changeSubscriptions.Add( serverConfigSub );

			// NetworkSettings 변경 감지
			var networkSub = _networkMonitor.OnChange((config, name) =>
			{
				var oldNetwork = _currentSnapshot?.Network;
				HandleConfigurationChange("NetworkSettings", oldNetwork, config);
			});
			_changeSubscriptions.Add( networkSub );

			// LoggingSettings 변경 감지
			var loggingSub = _loggingMonitor.OnChange((config, name) =>
			{
				var oldLogging = _currentSnapshot?.Logging;
				HandleConfigurationChange("LoggingSettings", oldLogging, config);
			});
			_changeSubscriptions.Add( loggingSub );

			// SecuritySettings 변경 감지
			var securitySub = _securityMonitor.OnChange((config, name) =>
			{
				var oldSecurity = _currentSnapshot?.Security;
				HandleConfigurationChange("SecuritySettings", oldSecurity, config);
			});
			_changeSubscriptions.Add( securitySub );

			// DatabaseSettings 변경 감지
			var databaseSub = _databaseMonitor.OnChange((config, name) =>
			{
				var oldDatabase = _currentSnapshot?.Database;
				HandleConfigurationChange("DatabaseSettings", oldDatabase, config);
			});
			_changeSubscriptions.Add( databaseSub );

			// JobQueueSettings 변경 감지
			var jobQueueSub = _jobQueueMonitor.OnChange((config, name) =>
			{
				var oldJobQueue = _currentSnapshot?.JobQueue;
				HandleConfigurationChange("JobQueueSettings", oldJobQueue, config);
			});
			_changeSubscriptions.Add( jobQueueSub );
		}

		// 설정 변경 처리
		private void HandleConfigurationChange( string sectionName, object oldValue, object newValue )
		{
			try
			{
				_logger.LogInformation( "설정 변경 감지: {SectionName}", sectionName );

				// 새 설정 검증
				if(!ValidateConfigurationChange( sectionName, newValue, out var errors ))
				{
					var errorMessage = string.Join(", ", errors);
					_logger.LogWarning( "설정 변경 거부 - 검증 실패: {Errors}", errorMessage );
					return;
				}

				// 스냅샷 업데이트
				var previousSnapshot = _currentSnapshot;
				_currentSnapshot = CreateSnapshot();

				// 이벤트 발생
				var changeArgs = new ConfigurationChangeEventArgs(sectionName, oldValue, newValue);
				ConfigurationChanged?.Invoke( this, changeArgs );

				// 등록된 콜백 실행
				foreach(var callback in _changeCallbacks)
				{
					try
					{
						callback( _currentSnapshot );
					}
					catch(Exception ex)
					{
						_logger.LogError( ex, "설정 변경 콜백 실행 중 오류 발생" );
					}
				}

				_logger.LogInformation( "설정 변경 처리 완료: {SectionName}", sectionName );
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "설정 변경 처리 중 오류 발생: {SectionName}", sectionName );
			}
		}

		// 설정 검증 메서드들
		public bool ValidateConfiguration( out List<string> errors )
		{
			errors = new List<string>();

			var networkValid = ValidateNetworkSettings(out var networkErrors);
			var loggingValid = ValidateLoggingSettings(out var loggingErrors);
			var securityValid = ValidateSecuritySettings(out var securityErrors);
			var databaseValid = ValidateDatabaseSettings(out var databaseErrors);
			var jobQueueValid = ValidateJobQueueSettings(out var jobQueueErrors);

			errors.AddRange( networkErrors );
			errors.AddRange( loggingErrors );
			errors.AddRange( securityErrors );
			errors.AddRange( databaseErrors );
			errors.AddRange( jobQueueErrors );

			return networkValid && loggingValid && securityValid && databaseValid && jobQueueValid;

		}

		public bool ValidateNetworkSettings( out List<string> errors )
		{
			errors = new List<string>();
			var validator = new Validators.NetworkSettingsValidator();
			var result = validator.Validate(string.Empty, NetworkSettings);

			if(result.Failed)
			{
				errors.AddRange( result.Failures );
			}

			return result.Succeeded;
		}

		public bool ValidateLoggingSettings( out List<string> errors )
		{
			errors = new List<string>();
			var validator = new Validators.LoggingSettingsValidator();
			var result = validator.Validate(string.Empty, LoggingSettings);

			if(result.Failed)
			{
				errors.AddRange( result.Failures );
			}

			return result.Succeeded;
		}

		public bool ValidateSecuritySettings( out List<string> errors )
		{
			errors = new List<string>();
			var validator = new Validators.SecuritySettingsValidator();
			var result = validator.Validate(string.Empty, SecuritySettings);

			if(result.Failed)
			{
				errors.AddRange( result.Failures );
			}

			return result.Succeeded;
		}

		public bool ValidateDatabaseSettings( out List<string> errors )
		{
			errors = new List<string>();
			var validator = new Validators.DatabaseSettingsValidator();
			var result = validator.Validate(string.Empty, DatabaseSettings);

			if(result.Failed)
			{
				errors.AddRange( result.Failures );
			}

			return result.Succeeded;
		}

		public bool ValidateJobQueueSettings( out List<string> errors )
		{
			errors = new List<string>();
			var validator = new Validators.JobQueueSettingsValidator();
			var result = validator.Validate(string.Empty, JobQueueSettings);

			if(result.Failed)
			{
				errors.AddRange( result.Failures );
			}

			return result.Succeeded;
		}

		// 특정 섹션 변경 검증
		private bool ValidateConfigurationChange( string sectionName, object newValue, out List<string> errors )
		{
			errors = new List<string>();

			return sectionName switch
			{
				"NetworkSettings" => ValidateNetworkSettings( out errors ),
				"LoggingSettings" => ValidateLoggingSettings( out errors ),
				"SecuritySettings" => ValidateSecuritySettings( out errors ),
				"DatabaseSettings" => ValidateDatabaseSettings( out errors ),
				"JobQueueSettings" => ValidateJobQueueSettings( out errors ),
				_ => true
			};
		}

		// 콜백 등록 관리
		public IDisposable RegisterChangeCallBack( Action<ServerConfiguration> callBack )
		{
			_changeCallbacks.Add( callBack );
			return new CallbackRegistration( () => RemoveCallBack( callBack ) );
		}

		public void UnregisterChangeCallBack(IDisposable registration)
		{
			registration?.Dispose();
		}

		private void RemoveCallBack(Action<ServerConfiguration> callBack)
		{
			// ConcurrentBag에서 제거는 복잡하므로 플래그 방식 사용
			// 실제 구현에서는 ConcurrentDictionary 사용 고려
		}

		// 스냅샷 관리
		public ServerConfiguration CreateSnapshot()
		{
			return new ServerConfiguration
			{
				Network = JsonSerializer.Deserialize<NetworkSettings>(
					JsonSerializer.Serialize( NetworkSettings ) ),
				Logging = JsonSerializer.Deserialize<LoggingSettings>(
					JsonSerializer.Serialize( LoggingSettings ) ),
				Security = JsonSerializer.Deserialize<SecuritySettings>(
					JsonSerializer.Serialize( SecuritySettings ) ),
				Database = JsonSerializer.Deserialize<DatabaseSettings>(
					JsonSerializer.Serialize( DatabaseSettings ) ),
				JobQueue = JsonSerializer.Deserialize<JobQueueSettings>(
					JsonSerializer.Serialize( JobQueueSettings ) )

			};
		}

		public bool CompareSnapshots(ServerConfiguration snapshot1, ServerConfiguration snapshot2)
		{
			if(snapshot1 == null || snapshot2 == null)
				return false;

			var json1 = JsonSerializer.Serialize( snapshot1 );
			var json2 = JsonSerializer.Serialize( snapshot2 );

			return json1.Equals( json2 );
		}

		// 설정 재로드
		public async Task ReloadConfigurationAsync()
		{
			await _initializationSemaphore.WaitAsync();
			try
			{
				_logger.LogInformation( "설정 재로드 시작" );

				// 현재 스냅샷 백업
				var previsousSnapshot = _currentSnapshot;

				// 새 스냅샷 생성
				_currentSnapshot = CreateSnapshot();

				// 변경 사항 검증
				if (!ValidateConfiguration(out var errors))
				{
					// 검증 실패 시 이전 스냅샷으로 롤백
					_currentSnapshot = previsousSnapshot;
					var errorMessage = string.Join(Environment.NewLine, errors);
					_logger.LogError( "설정 재로드 실패 - 검증 오류:{NewLine}{Errors}", Environment.NewLine, errorMessage );
					throw new InvalidOperationException( $"설정 재로드 실패: {errorMessage}" );
				}
			}
			finally
			{
				_initializationSemaphore.Release();
			}
		}

		// 리소스 정리
		public void Dispose()
		{
			if(_disposed) return;

			_logger.LogInformation( "ConfigurationService 정리 시작" );

			// 모든 변경 감지 구독 해제
			foreach(var subscription in _changeSubscriptions)
			{
				subscription?.Dispose();
			}

			_initializationSemaphore?.Dispose();
			_disposed = true;

			_logger.LogInformation( "ConfigurationService 정리 완료" );
		}

		// 내부 클래스: 콜백 등록 관리
		private class CallbackRegistration : IDisposable
		{
			private readonly Action _disposeAction;
			private bool _disposed;

			public CallbackRegistration(Action disposeAction)
			{
				_disposeAction = disposeAction;
			}

			public void Dispose()
			{
				if(!_disposed)
				{
					_disposeAction?.Invoke();
					_disposed = true;
				}
			}
		}
	}
}
