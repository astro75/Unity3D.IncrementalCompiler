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
		logger?.AppendHeader();

        logger?.Append("mono path");
        logger?.Append(Environment.GetEnvironmentVariable("MONO_PATH"));

        var platform = CurrentPlatform;

		var responseFile = args[0];
		var compilationOptions = File.ReadAllLines(responseFile.TrimStart('@'));
        var targetProfileDir = GetTargetProfileDir(compilationOptions);
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
		logger?.Append($"Target profile directory: {targetProfileDir}");
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

		var stopwatch = Stopwatch.StartNew();
		int exitCode = compiler.Compile(platform, unityEditorDataDir, targetProfileDir, responseFile);
		stopwatch.Stop();

		logger?.Append($"Elapsed time: {stopwatch.ElapsedMilliseconds / 1000f:F2} sec");
        // this line will be parsed by code in CompilerSettings.cs
		logger?.Append($"compilation-info;{targetAssembly};{stopwatch.ElapsedMilliseconds};{DateTime.UtcNow:O}");
		logger?.Append("");
		compiler.PrintCompilerOutputAndErrors();
        return exitCode;
	}


    // TODO: clean this mess
    static Compiler CreateCompiler(Logger? logger, string projectDir, Platform platform)
    {
        logger?.Append("Create Compiler");

        if (platform == Platform.Windows) {
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

	private static Platform CurrentPlatform
	{
		get
		{
			switch (Environment.OSVersion.Platform)
			{
				case PlatformID.Unix:
					// Well, there are chances MacOSX is reported as Unix instead of MacOSX.
					// Instead of platform check, we'll do a feature checks (Mac specific root folders)
					if (Directory.Exists("/Applications")
						& Directory.Exists("/System")
						& Directory.Exists("/Users")
						& Directory.Exists("/Volumes"))
					{
						return Platform.Mac;
					}
					return Platform.Linux;

				case PlatformID.MacOSX:
					return Platform.Mac;

				default:
					return Platform.Windows;
			}
		}
	}

	/// <summary>
	/// Returns the directory that contains Mono and MonoBleedingEdge directories
	/// </summary>
	private static string GetUnityEditorDataDir()
	{
		// Windows:
		// UNITY_DATA: C:\Program Files\Unity\Editor\Data\Mono
		//
		// Mac OS X:
		// UNITY_DATA: /Applications/Unity/Unity.app/Contents/Frameworks/Mono

		return Environment.GetEnvironmentVariable("UNITY_DATA").Replace("\\", "/");
    }

	private static string GetTargetProfileDir(string[] compilationOptions)
	{
		/* Looking for something like
		-r:"C:\Program Files\Unity\Editor\Data\Mono\lib\mono\unity\System.Xml.Linq.dll"
		or
		-r:'C:\Program Files\Unity\Editor\Data\Mono\lib\mono\unity\System.Xml.Linq.dll'
		*/

		var reference = compilationOptions.First(line =>
            (
                line.StartsWith("-r:", StringComparison.Ordinal)
                || line.StartsWith("-reference:", StringComparison.Ordinal)
            )
            && line.Contains("System.Xml.Linq.dll")
        );

        var replaced = reference.Replace("'", "").Replace("\"", "");
		var systemXmlLinqPath = replaced.Substring(replaced.IndexOf(":", StringComparison.Ordinal) + 1);
		var profileDir = Path.GetDirectoryName(systemXmlLinqPath);
		return profileDir;
	}

	private static string GetExecutingAssemblyFileVersion()
	{
		var assembly = Assembly.GetExecutingAssembly();
		var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
		return fvi.FileVersion;
	}
}
