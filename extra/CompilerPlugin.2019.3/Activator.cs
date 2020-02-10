using System.Reflection;
using UnityEditor;
using UnityEditor.Scripting.Compilers;
using Harmony;
using UnityEditor.Scripting.ScriptCompilation;

[InitializeOnLoad]
public static class CSharp60SupportActivator
{
    static CSharp60SupportActivator()  {
        // Unity 2017+
        var harmony = HarmonyInstance.Create("CSharpVNextSupport");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
}


[HarmonyPatch(typeof(CSharpLanguage))]
[HarmonyPatch("CreateCompiler")]
internal static class Patch
{
    private static bool Prefix(ref ScriptCompilerBase __result, ScriptAssembly scriptAssembly, EditorScriptCompilationOptions options, string tempOutputDirectory)
    {
        __result = new CustomCSharpCompiler(scriptAssembly, options, tempOutputDirectory);
        return false;
    }
}
