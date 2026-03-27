using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Config;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Infra
{
	/// <summary>
	/// 모든 인프라 주기 작업을 중앙에서 관리하는 서비스
	/// ServerHost에서 Start()/Stop()을 호출하여 생명주기를 관리합니다.
	/// </summary>
	public class TickService : IDisposable
	{
		private readonly ILogger<TickService> _logger;
		private readonly int _baseTickMs;
		private Timer _timer;
		private volatile TickSubscription[] _snapshot = Array.Empty<TickSubscription>();
		private readonly List<TickSubscription> _subscription = new();
		private readonly object _subLock = new object();

		public TickService(ILogger<TickService> logger, IOptions<ServerSettings> settings)
		{
			_logger = logger;
			_baseTickMs = settings.Value.Tick.BaseTickMs;
		}

		public void Register(string name, int intervalMs, Action callback)
		{
			if(_timer != null)
				throw new InvalidOperationException( "Cannot register after Start() has been called" );

			if(string.IsNullOrEmpty( name ))
				throw new ArgumentNullException( nameof( name ) );

			if(intervalMs < _baseTickMs)
				throw new ArgumentOutOfRangeException( nameof( intervalMs ), $"Interval ({intervalMs}ms) must be >= BaseTickMs ({_baseTickMs}ms)" );

			if(callback == null)
				throw new ArgumentNullException( nameof( callback ) );

			lock(_subLock)
			{
				_subscription.Add( new TickSubscription( name, intervalMs, callback, _logger ) );
				_snapshot = _subscription.ToArray(); // 등록 시에만 복제
			}

			_logger.LogInformation( "Tick registered: {Name}, interval={Interval}ms", name, intervalMs );

		}

		public void Start()
		{
			if(_timer != null)
				throw new InvalidOperationException( "TickServerce is already started" );

			long now = Environment.TickCount64;
			foreach(TickSubscription sub in _subscription)
			{
				sub.ResetTime( now );
			}

			_timer = new Timer( _ => Tick(), null, _baseTickMs, _baseTickMs );
			_logger.LogInformation( "TickService started. Base tick: {BaseTickMs}ms, subscriptions: {count}", _baseTickMs, _subscription.Count );
		}

		public void Stop()
		{
			_timer.Dispose();
			_timer = null;
			_logger.LogInformation( "TickService stopped" );
		}

		private void Tick()
		{
			long now = Environment.TickCount64;
			TickSubscription[] items = _snapshot;	// volatile read, lock 없음
			foreach(TickSubscription sub in items)
			{
				sub.TryTick( now );
			}
		}

		public void Dispose() => _timer?.Dispose();
	}

	internal class TickSubscription
	{
		private readonly string _name;
		private readonly Action _callback;
		private readonly ILogger _logger;
		private long _lastTickTime;

		public int IntervalMs { get; }

		public TickSubscription(string name, int intervalMs, Action callback, ILogger logger )
		{
			_name = name;
			IntervalMs = intervalMs;
			_callback = callback;
			_logger = logger;
			_lastTickTime = Environment.TickCount64;
		}

		public void TryTick(long now)
		{
			long lastTime = Volatile.Read(ref _lastTickTime);
			if(now - lastTime < IntervalMs) 
				return;

			// CAS(Compare-And-Swap): lastTime과 같을 때만 now로 교체
			// 성공한 스레드만 콜백 실행 권한 획득 -> 중복 실행 방지
			if(Interlocked.CompareExchange( ref _lastTickTime, now, lastTime ) != lastTime)
				return;

			try
			{
				_callback();
			}
			catch(Exception ex)
			{
				_logger.LogError( ex, "[Tick] {Name} threw", _name );
			}
		}

		public void ResetTime(long now)
		{
			Volatile.Write( ref _lastTickTime, now );
		}
	}
}
