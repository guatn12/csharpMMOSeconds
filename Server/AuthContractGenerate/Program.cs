using System.Diagnostics;

namespace AuthContractGenerate
{
	internal static class Program
	{
		private static async Task<int> Main( string[] args )
		{
			if(args.Length != 2 || args[ 0 ] != "--repo-root")
				throw new ArgumentException( "Usage: AuthContractGenerate --repo-root <path>" );

			string repoRoot = Path.GetFullPath( args[ 1 ] );
			string projectPath = Path.Combine( repoRoot, "Server", "AuthContractGenerate",
				"AuthContractGenerate.csproj" );
			string protoPath = Path.Combine( repoRoot, "Common", "Protobuf", "Auth", "AuthInternal.proto" );

			if(!File.Exists( projectPath ))
				throw new FileNotFoundException( "Generator project was not found.", projectPath );
			if(!File.Exists( protoPath ))
				throw new FileNotFoundException( "AuthInternal.proto was not found.", protoPath );

			var outputs = new (string Role, string Directory)[]
			{
				("Client", Path.Combine( repoRoot, "Server", "Server", "Services", "Auth", "Generated" )),
				("Server", Path.Combine( repoRoot, "Server", "AuthServer", "Grpc", "Generated" ))
			};

			foreach(var (role, outputDirectory) in outputs)
			{
				Directory.CreateDirectory( outputDirectory );
				string dependencyDirectory = Path.Combine( repoRoot, "Server", "AuthContractGenerate", "obj",
					"auth-contracts", role );
				Directory.CreateDirectory( dependencyDirectory );

				var startInfo = new ProcessStartInfo( "dotnet" )
				{
					UseShellExecute = false, WorkingDirectory = repoRoot
				};
				startInfo.ArgumentList.Add( "msbuild" );
				startInfo.ArgumentList.Add( projectPath );
				startInfo.ArgumentList.Add( "-target:GenerateAuthContracts" );
				startInfo.ArgumentList.Add( "-property:AuthContractGeneration=true" );
				startInfo.ArgumentList.Add( $"-property:AuthContractRole={role}" );
				startInfo.ArgumentList.Add( $"-property:AuthContractOutputDir={outputDirectory}" );
				startInfo.ArgumentList.Add(
					$"-property:Protobuf_DepFilesPath={dependencyDirectory}{Path.DirectorySeparatorChar}" );

				using var process = Process.Start( startInfo )
				                    ?? throw new InvalidOperationException( "Failed to start dotnet msbuild." );
				await process.WaitForExitAsync();

				if(process.ExitCode != 0)
					return process.ExitCode;
			}

			return 0;
		}
	}
}
