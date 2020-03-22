using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Scripting.Compilers;
using UnityEditor.Scripting.ScriptCompilation;

[InitializeOnLoad]
public static class CSharp60SupportActivator
{
    static CSharp60SupportActivator()  {
        // Unity 2017+
        try
        {
            var language = new CustomCSharpLanguage();
            var list = UnityEditor.Scripting.ScriptCompilers.SupportedLanguages;
            list.RemoveAll(l => l.GetType() == typeof (CSharpLanguage));
            list.Add(language);
            {
                var type = typeof(UnityEditor.Scripting.ScriptCompilers);
                var field = type.GetField(nameof(UnityEditor.Scripting.ScriptCompilers.CSharpSupportedLanguage),
                    BindingFlags.Static | BindingFlags.NonPublic);
                field.SetValue(null, (SupportedLanguage) language);
            }
            {
                // EditorBuildRules gets initialized before we can change the compiler
                // so we replace compiler reference here
                foreach (var targetAssembly in EditorBuildRules.GetPredefinedTargetAssemblies())
                {
                    targetAssembly.Language = language;
                }
            }
        }
        catch (Exception e)
        {
            throw new ApplicationException($"Compiler initialization error", e);
        }
    }
}

internal class CustomCSharpLanguage : CSharpLanguage
{
    public override ScriptCompilerBase CreateCompiler(
        ScriptAssembly scriptAssembly, EditorScriptCompilationOptions options, string tempOutputDirectory) {
        return new CustomCSharpCompiler(scriptAssembly, options, tempOutputDirectory);
    }
}
