// See https://aka.ms/new-console-template for more information
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace Server.Benchmarks
{
	public class Program
	{
		public static void Main(string[] args)
		{
			// 옵션 1: 모든 벤치마크 실행
			// var summary = BenchmarkRunner.Run<PacketSerializationBenchmark>();

			// 옵션 2: 여러 벤치마크 실행
			// var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

			// 옵션 3: 커스텀 설정
			var config = DefaultConfig.Instance
				.AddJob(Job.Default
					.WithToolchain(InProcessEmitToolchain.Instance)); // In-process 실행

			BenchmarkSwitcher
				.FromAssembly( typeof( Program ).Assembly )
				.Run( args, config );

			// 결과는 BenchmarkDotNet.Artifacts/results 폴더에 저장됩니다.
		}
	}
}