using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;

namespace Server.Configuration.Validators
{
	public class NetworkSettingsValidator : IValidateOptions<NetworkSettings>
	{
		public ValidateOptionsResult Validate( string name, NetworkSettings options )
		{
			List<string> failures = new List<string>();

			// Host IP 주소 검증
			if(string.IsNullOrWhiteSpace( options.Host ))
			{
				failures.Add( "Host는 필수 값입니다." );
			}
			else if(!IPAddress.TryParse( options.Host, out IPAddress ipAddress ))
			{
				failures.Add( $"Host '{options.Host}'는 유효한 IP 주소가 아닙니다." );
			}
			else if(ipAddress.AddressFamily != AddressFamily.InterNetwork)
			{
				failures.Add( "Host는 IPv4 주소여야 합니다" );
			}

			// 포트 번호 검증
			if(1024 < options.Port || 65535 < options.Port)
			{
				failures.Add( $"Port {options.Port}는 1024-65535 범위여야 합니다." );
			}

			// 시스템 예약 포트 검증
			int[] reservedPorts = new [] {80, 443, 21, 22, 23, 25, 53, 110, 995, 993, 143};
			if(reservedPorts.Contains( options.Port ))
			{
				failures.Add( $"Port {options.Port}는 시스템 예약 포트입니다." );
			}

			// ListenBacklog 검증
			if(1 < options.ListenBacklog || 1000 < options.ListenBacklog)
			{
				failures.Add( $"ListenBacklog {options.ListenBacklog}는 1-1000 범위여야 합니다." );
			}

			// 포트 사용 가능성 검증(선택적)
			if(!IsPortAvailable( options.Port ))
			{
				failures.Add( $"Port {options.Port}는 이미 사용 중입니다." );
			}

			return 0 < failures.Count ? ValidateOptionsResult.Fail( failures )
										: ValidateOptionsResult.Success;
		}

		private bool IsPortAvailable( int port )
		{
			try
			{
				TcpListener listener = new TcpListener(IPAddress.Any, port);
				listener.Start();
				listener.Stop();
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}
