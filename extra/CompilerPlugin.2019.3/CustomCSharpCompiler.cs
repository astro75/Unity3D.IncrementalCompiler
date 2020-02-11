using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEditor;
using UnityEditor.Scripting.Compilers;
using UnityEditor.Scripting.ScriptCompilation;
using UnityEditor.Utils;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class CompilationFlags {
    public static bool checkIfBuildCompiles = false;
}

internal class CustomCSharpCompiler : MicrosoftCSharpCompiler
{
    public const string COMPILER_DEFINE = "ALWAYS_ON";

    public CustomCSharpCompiler(ScriptAssembly scriptAssembly, EditorScriptCompilationOptions options, string tempOutputDirectory)
        : base(scriptAssembly, options, tempOutputDirectory)
    {
    }

	string GetUniversalCompilerPath()
	{
		var basePath = Path.Combine(Directory.GetCurrentDirectory(), "Compiler");
		var compilerPath = Path.Combine(basePath, "UniversalCompiler.exe");
		return File.Exists(compilerPath) ? compilerPath : null;
	}

    static void FillCompilerOptionsEdited(
        List<string> arguments,
        bool buildingForEditor,
        BuildTarget BuildTarget)
    {
        arguments.Add("-nostdlib+");
        arguments.Add("-preferreduilang:en-US");
        arguments.Add("-langversion:7.3");
    }

    static string GenerateResponseFileEdited(
      ScriptAssembly assembly,
      EditorScriptCompilationOptions options,
      string tempBuildDirectory)
    {
      bool buildingForEditor = (options & EditorScriptCompilationOptions.BuildingForEditor) == EditorScriptCompilationOptions.BuildingForEditor;
      bool flag = (options & EditorScriptCompilationOptions.BuildingDevelopmentBuild) == EditorScriptCompilationOptions.BuildingDevelopmentBuild;
      List<string> arguments = new List<string>()
      {
        "-target:library",
        "-nowarn:0169",
        "-out:" + PrepareFileName(AssetPath.Combine(tempBuildDirectory, assembly.Filename)),
        "-unsafe" // added unsafe to all projects, because setting in unity asmdef does not work
      };

      // added
      arguments.Add("-define:" + COMPILER_DEFINE);
      arguments.Add("-define:__UNITY_PROCESSID__" + Process.GetCurrentProcess().Id);

      if (assembly.CompilerOptions.AllowUnsafeCode)
        arguments.Add("-unsafe");
      arguments.Add("-debug:portable");
      if (!flag && (!buildingForEditor || !EditorPrefs.GetBool("AllowAttachedDebuggingOfEditor", true)))
        arguments.Add("-optimize+");
      else
        arguments.Add("-optimize-");
      FillCompilerOptionsEdited(arguments, buildingForEditor, assembly.BuildTarget);
      foreach (ScriptAssembly assemblyReference in assembly.ScriptAssemblyReferences)
        arguments.Add("-reference:" + ScriptCompilerBase.PrepareFileName(AssetPath.Combine(assembly.OutputDirectory, assemblyReference.Filename)));
      foreach (string reference in assembly.References)
        arguments.Add("-reference:" + ScriptCompilerBase.PrepareFileName(reference));
      foreach (string str in ((IEnumerable<string>) assembly.Defines).Distinct<string>())
        arguments.Add("-define:" + str);
      foreach (string file in assembly.Files)
      {
        string str = Paths.UnifyDirectorySeparator(ScriptCompilerBase.PrepareFileName(file));
        arguments.Add(str);
      }
      HashSet<string> source = new HashSet<string>((IEnumerable<string>) (assembly.Language?.CreateResponseFileProvider()?.Get(assembly.OriginPath) ?? new List<string>()));
      string path = source.SingleOrDefault<string>((Func<string, bool>) (x => ((IEnumerable<string>) CompilerSpecificResponseFiles.MicrosoftCSharpCompilerObsolete).Contains<string>(AssetPath.GetFileName(x))));
      if (!string.IsNullOrEmpty(path))
        UnityEngine.Debug.LogWarning((object) ("Using obsolete custom response file '" + AssetPath.GetFileName(path) + "'. Please use 'csc.rsp' instead."));
      foreach (string responseFileName in source)
        ScriptCompilerBase.AddResponseFileToArguments(arguments, responseFileName, assembly.CompilerOptions.ApiCompatibilityLevel);
      return CommandLineFormatter.GenerateResponseFile((IEnumerable<string>) arguments);
    }

	protected override Program StartCompiler()
	{
        var universalCompilerPath = GetUniversalCompilerPath();
		if (universalCompilerPath != null)
		{
            if (assembly.GeneratedResponseFile == null)
                assembly.GeneratedResponseFile = GenerateResponseFileEdited(assembly, options, tempOutputDirectory);

            Program StartCompilerLocal(string compiler) {
                var p = new Program(CreateOSDependentStartInfo(
                    isWindows: Application.platform == RuntimePlatform.WindowsEditor,
                    processPath: compiler,
                    // Arguments = "/noconfig @" + this.assembly.GeneratedResponseFile,
                    processArguments: " @" + assembly.GeneratedResponseFile,
                    unityEditorDataDir: MonoInstallationFinder.GetFrameWorksFolder()
                ));
                p.Start();
                return p;
            }

            var program = StartCompilerLocal(universalCompilerPath);

		    if (!CompilationFlags.checkIfBuildCompiles) return program;

            var compiledDllName = assembly.Filename.Split('/').Last();
            if (compiledDllName != "Assembly-CSharp.dll") return program;

		    program.WaitForExit();
            if (program.ExitCode != 0) return program;

		    // message contents are used in CI script, so this shouldnt be changed
		    Debug.Log("Scripts successfully compile in Build mode");
		    // CI script expects to find log from above if process was killed
		    // sometimes process.Kill() happens faster than Debug.Log() logs our message
		    // sleeping the thread ensures that message was logged before we kill the process
		    Thread.Sleep(5000);

		    Process.GetCurrentProcess().Kill();
		    throw new Exception("unreachable code");
		}
		else
		{
			// fallback to the default compiler.
			Debug.LogWarning($"Universal C# compiler not found in project directory. Use the default compiler");
			return base.StartCompiler();
		}
    }

    static ProcessStartInfo CreateOSDependentStartInfo(
        bool isWindows, string processPath, string processArguments, string unityEditorDataDir
    ) {
        ProcessStartInfo startInfo;

        if (isWindows)
        {
            startInfo = new ProcessStartInfo(processPath, processArguments);
        }
        else
        {
            string runtimePath;

            if (File.Exists("/Library/Frameworks/Mono.framework/Commands/mono"))
            {
                runtimePath = "/Library/Frameworks/Mono.framework/Commands/mono";
            }
            else if (File.Exists("/usr/local/bin/mono"))
            {
                runtimePath = "/usr/local/bin/mono";
            }
            else
            {
                runtimePath = Path.Combine(unityEditorDataDir, "MonoBleedingEdge/bin/mono");
            }
            startInfo = new ProcessStartInfo(runtimePath, $"{CommandLineFormatter.PrepareFileName(processPath)} {processArguments}");
        }

        var vars = startInfo.EnvironmentVariables;
        vars.Add("UNITY_DATA", unityEditorDataDir);

        startInfo.CreateNoWindow = true;
        startInfo.WorkingDirectory = Application.dataPath + "/..";
        startInfo.RedirectStandardError = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.UseShellExecute = false;

        return startInfo;
    }
}
