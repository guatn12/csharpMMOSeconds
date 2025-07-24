using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace ServerCore
{
	// 접근 용이성.
	public static class LogManager
	{
		public static void Init()
		{
			Log.Logger = new LoggerConfiguration()
				.MinimumLevel.Debug()				//최소 로그 레벨
				.WriteTo.Console(outputTemplate:
				"[{Timestamp:HH:mm:ss} {Level:u3} {Message:lj}{Exception}]{NewLine}" )					// 콘솔에 로그 출력, 콘솔 출력 템플릿 지정
				.WriteTo.File( "logs/log-.txt", rollingInterval: RollingInterval.Day,
				outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {Message:lj}{Exception}]{NewLine}" )	// 파일에 로그 출력, 파일 출력 템플릿 지정
				.CreateLogger();					// 로그 생성.
		}

		// --------  간단한 메세지 전송 로그 ------------
		public static void Info(string message) => Log.Information(message);
		public static void Debug(string message) => Log.Debug(message);
		public static void Warning(string message) => Log.Warning(message);

		// --------  구조화된 로그 형식 ------------
		public static void Info( string messageTemplate, params object[] propertyValues ) => Log.Information( messageTemplate, propertyValues );
		public static void Debug( string messageTemplate, params object[] propertyValues ) => Log.Debug( messageTemplate, propertyValues );
		public static void Warning( string messageTemplate, params object[] propertyValues ) => Log.Warning( messageTemplate, propertyValues );

		// ------------------- 심각한 로그의 예외 포함 간단 로그 ----------------------------------
		public static void Error(string message, Exception ex = null) => Log.Error(message, ex);
		public static void Fatal(string message, Exception ex = null) => Log.Fatal(message, ex);

		// ------------------- 심각한 로그의 구조화된 로그 ----------------------------------
		public static void Error( Exception ex, string messageTemplate, params object[] propertyValues ) => Log.Error( ex, messageTemplate, propertyValues );
		public static void Fatal( Exception ex, string messageTemplate, params object[] propertyValues ) => Log.Fatal( ex, messageTemplate, propertyValues );

		public static void CloseAndFlush() => Log.CloseAndFlush();
	}
}
