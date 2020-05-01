// logging lib crashes compiler
#define LOGGING_ENABLED

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

internal class Program
{
    const string INCREMENTAL_COMPILER_DIR = "Compiler";
    const string ROSLYN_DIR = "Roslyn";

	private static int Main(string[] args)
	{
		int exitCode;
		Logger? logger = null;

#if LOGGING_ENABLED
		using (logger = new Logger())
#endif
		{
			try
			{
				exitCode = Compile(args, logger);
			}
			catch (Exception e)
			{
				exitCode = -1;
				Console.Error.Write($"Compiler redirection error: {e.GetType()}{Environment.NewLine}{e.Message} {e.StackTrace}");
			}
		}

		return exitCode;
	}

	private static int Compile(string[] args, Logger logger)
    {
        var startTime = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();

		logger?.AppendHeader();

        logger?.Append("mono path");
        logger?.Append(Environment.GetEnvironmentVariable("MONO_PATH"));

        var platform = CurrentPlatform;

		var responseFile = args[0];
		var compilationOptions = File.ReadAllLines(responseFile.TrimStart('@'));
        var unityEditorDataDir = GetUnityEditorDataDir();
        var projectDir = Directory.GetCurrentDirectory();
        var targetAssembly = compilationOptions.First(line => line.StartsWith("-out:"))
												  .Replace("'", "")
												  .Replace("\"", "")
												  .Substring(10);

		logger?.Append($"CSharpCompilerWrapper.exe version: {GetExecutingAssemblyFileVersion()}");
		logger?.Append($"Platform: {platform}");
		logger?.Append($"Target assembly: {targetAssembly}");
		logger?.Append($"Project directory: {projectDir}");
		logger?.Append($"Unity 'Data' or 'Frameworks' directory: {unityEditorDataDir}");


		if (platform == Platform.Linux)
		{
			logger?.Append("");
			logger?.Append("Platform is not supported");
			return -1;
		}

	    var compiler = CreateCompiler(logger, projectDir, platform);

        logger?.Append($"Compiler: {compiler.Name}");
		logger?.Append("");
		logger?.Append("- Compilation -----------------------------------------------");
		logger?.Append("");

		var exitCode = compiler.Compile(platform, unityEditorDataDir, responseFile);
		sw.Stop();

		logger?.Append($"Elapsed time: {sw.ElapsedMilliseconds / 1000f:F2} sec");
        // this line will be parsed by code in CompilerSettings.cs
		logger?.Append($"compilation-info;{targetAssembly};{sw.ElapsedMilliseconds};{DateTime.UtcNow:O}");
		logger?.Append("");

        {
            var fileName = SharedData.CompileTimesFileName(targetAssembly);
            try
            {
                File.WriteAllLines(fileName, new [] {
                    startTime.ToString("O"),
                    sw.ElapsedMilliseconds.ToString(),
                    exitCode.ToString()
                });
            }
            catch (Exception e)
            {
                logger?.Append(e.ToString());
            }
        }

		compiler.PrintCompilerOutputAndErrors();
        return exitCode;
	}

    static Compiler CreateCompiler(Logger? logger, string projectDir, Platform platform)
    {
        logger?.Append("Create Compiler");

        {
            var compilerDirectory = Path.Combine(projectDir, ROSLYN_DIR, "net472");
            if (File.Exists(Path.Combine(compilerDirectory, RoslynCompiler.ExeName)))
            {
                logger?.Append("Compiler directory: " + compilerDirectory);
                return new RoslynCompiler(logger, compilerDirectory);
            }
        }

        {
            var compilerDirectory = Path.Combine(projectDir, ROSLYN_DIR, "netcoreapp3.1");
            if (File.Exists(Path.Combine(compilerDirectory, DotnetRoslynCompiler.ExeName)))
            {
                logger?.Append("Compiler directory: " + compilerDirectory);
                return new DotnetRoslynCompiler(logger, compilerDirectory);
            }
        }

        {
            var compilerDirectory = Path.Combine(projectDir, INCREMENTAL_COMPILER_DIR);
            logger?.Append("Compiler directory: " + compilerDirectory);
            return new IncrementalCompiler(logger, compilerDirectory);
        }
    }

	static Platform CurrentPlatform =>
        Environment.OSVersion.Platform switch {
            PlatformID.Unix => Platform.Linux,
            PlatformID.MacOSX => Platform.Mac,
            _ => Platform.Windows
        };

    /// <summary>
	/// Returns the directory that contains Mono and MonoBleedingEdge directories
	/// </summary>
	static string GetUnityEditorDataDir()
	{
		// Windows:
		// UNITY_DATA: C:\Program Files\Unity\Editor\Data\Mono
		//
		// Mac OS X:
		// UNITY_DATA: /Applications/Unity/Unity.app/Contents/Frameworks/Mono

		return Environment.GetEnvironmentVariable("UNITY_DATA").Replace("\\", "/");
    }

	static string GetExecutingAssemblyFileVersion()
	{
		var assembly = Assembly.GetExecutingAssembly();
		var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
		return fvi.FileVersion;
	}
}
