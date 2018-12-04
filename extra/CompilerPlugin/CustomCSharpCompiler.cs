using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Scripting;
using UnityEditor.Scripting.Compilers;
using UnityEditor.Utils;
using UnityEngine;

internal class CustomCSharpCompiler : MonoCSharpCompiler
{
    public const string COMPILER_DEFINE = "ALWAYS_ON";

    MonoIsland island => GetIsland();

#if UNITY4
	public CustomCSharpCompiler(MonoIsland island, bool runUpdater) : base(island)
    {
	}
#else
	public CustomCSharpCompiler(MonoIsland island, bool runUpdater) : base(island, runUpdater)
	{
	}
#endif

    MonoIsland GetIsland() {
        const BindingFlags bindingAttr =
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.FlattenHierarchy;
        var field = GetType().GetField("_island", bindingAttr) ?? GetType().GetField("m_Island", bindingAttr);
        if (field == null) {
            throw new NotSupportedException("Cannot get _island or m_Island field from MonoCSharpCompiler. Did the internal API change?");
        }
        return (MonoIsland)field.GetValue(this);
    }

    private string[] GetAdditionalReferences()
	{
		// calling base method via reflection
		var bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;
		var methodInfo = GetType().BaseType.GetMethod(nameof(GetAdditionalReferences), bindingFlags);
        if (methodInfo == null) return null;
		var result = (string[])methodInfo.Invoke(this, null);
		return result;
    }

    private string GetCompilerPath(List<string> arguments)
	{
		// calling base method via reflection
		var bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;
		var methodInfo = GetType().BaseType.GetMethod(nameof(GetCompilerPath), bindingFlags);
		var result = (string)methodInfo.Invoke(this, new object[] {arguments});
		return result;
	}

	private string GetUniversalCompilerPath()
	{
		var basePath = Path.Combine(Directory.GetCurrentDirectory(), "Compiler");
		var compilerPath = Path.Combine(basePath, "UniversalCompiler.exe");
		return File.Exists(compilerPath) ? compilerPath : null;
	}

	// Copy of MonoCSharpCompiler.StartCompiler()
	// The only reason it exists is to call the new implementation
	// of GetCompilerPath(...) which is non-virtual unfortunately.
	protected override Program StartCompiler()
	{
		var arguments = new List<string>
		{
			"-debug",
			"-target:library",
			"-nowarn:0169",
			"-out:" + PrepareFileName(island._output),
			"-unsafe"
		};

	    arguments.Add("-define:" + COMPILER_DEFINE);

        var unity5References = GetAdditionalReferences();
        if (unity5References != null)
        {
            foreach (string path in unity5References)
            {
                var text = Path.Combine(GetProfileDirectoryViaReflection(), path);
                if (File.Exists(text))
                {
                    arguments.Add("-r:" + PrepareFileName(text));
                }
            }
        }
        else
        {
            // Unity 2017+
//            foreach (var reference in island._references)
//            {
//                arguments.Add("-r:" + PrepareFileName(reference));
//            }
        }


		foreach (var define in island._defines.Distinct())
		{
			arguments.Add("-define:" + define);
		}

		foreach (var file in island._files)
		{
			arguments.Add(PrepareFileName(file));
		}

        foreach (string fileName in island._references)
        {
            arguments.Add("-r:" + PrepareFileName(fileName));
        }

        var universalCompilerPath = GetUniversalCompilerPath();
		if (universalCompilerPath != null)
		{
			// use universal compiler.
			arguments.Add("-define:__UNITY_PROCESSID__" + System.Diagnostics.Process.GetCurrentProcess().Id);

			// this function should be run because it adds an item to arguments
			//var compilerPath = GetCompilerPath(arguments);

			var rspFileName = "Assets/mcs.rsp";
			if (File.Exists(rspFileName))
			{
				arguments.Add("@" + rspFileName);
			}
			//else
			//{
			//	var defaultCompilerName = Path.GetFileNameWithoutExtension(compilerPath);
			//	rspFileName = "Assets/" + defaultCompilerName + ".rsp";
			//	if (File.Exists(rspFileName))
			//		arguments.Add("@" + rspFileName);
			//}

			return StartCompiler(island._target, universalCompilerPath, arguments);
		}
		else
		{
			// fallback to the default compiler.
			Debug.LogWarning($"Universal C# compiler not found in project directory. Use the default compiler");
			return base.StartCompiler();
		}
	}

	// In Unity 5.5 and earlier GetProfileDirectory() was an instance method of MonoScriptCompilerBase class.
	// In Unity 5.6 the method is removed and the profile directory is detected differently.
	private string GetProfileDirectoryViaReflection()
	{
		var monoScriptCompilerBaseType = typeof(MonoScriptCompilerBase);
		var getProfileDirectoryMethodInfo = monoScriptCompilerBaseType.GetMethod("GetProfileDirectory", BindingFlags.NonPublic | BindingFlags.Instance);
		if (getProfileDirectoryMethodInfo != null)
		{
			// For any Unity version prior to 5.6
			string result = (string)getProfileDirectoryMethodInfo.Invoke(this, null);
			return result;
		}

		// For Unity 5.6
		var monoIslandType = typeof(MonoIsland);
		var apiCompatibilityLevelFieldInfo = monoIslandType.GetField("_api_compatibility_level");
		var apiCompatibilityLevel = (ApiCompatibilityLevel)apiCompatibilityLevelFieldInfo.GetValue(island);

		string profile;
		if (apiCompatibilityLevel != ApiCompatibilityLevel.NET_2_0)
		{
			profile = GetMonoProfileLibDirectory(apiCompatibilityLevel);
		}
		else
		{
			profile = "2.0-api";
		}

		string profileDirectory = GetProfileDirectory(profile, "MonoBleedingEdge");
		return profileDirectory;
	}

	private static string GetMonoProfileLibDirectory(ApiCompatibilityLevel apiCompatibilityLevel)
	{
		var buildPipelineType = typeof(BuildPipeline);
		var compatibilityProfileToClassLibFolderMethodInfo = buildPipelineType.GetMethod("CompatibilityProfileToClassLibFolder", BindingFlags.NonPublic | BindingFlags.Static);
		string profile = (string)compatibilityProfileToClassLibFolderMethodInfo.Invoke(null, new object[] { apiCompatibilityLevel });

		var apiCompatibilityLevelNet46 = (ApiCompatibilityLevel)3;
		string monoInstallation = apiCompatibilityLevel != apiCompatibilityLevelNet46 ? "Mono" : "MonoBleedingEdge";
		return GetProfileDirectory(profile, monoInstallation);
	}

	private static string GetProfileDirectory(string profile, string monoInstallation)
	{
		string monoInstallation2 = MonoInstallationFinder.GetMonoInstallation(monoInstallation);
		return Path.Combine(monoInstallation2, Path.Combine("lib", Path.Combine("mono", profile)));
	}
}
