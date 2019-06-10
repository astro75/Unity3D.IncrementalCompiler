using UnityEditor;
using UnityEditor.Modules;
using UnityEditor.Scripting;
using UnityEditor.Scripting.Compilers;
using UnityEditor.Scripting.ScriptCompilation;

internal class CustomCSharpLanguage : CSharpLanguage
{
    public override ScriptCompilerBase CreateCompiler(
        ScriptAssembly scriptAssembly, MonoIsland island, bool buildingForEditor, BuildTarget targetPlatform,
        bool runUpdater
    ) {
        // This method almost exactly copies CSharpLanguage.CreateCompiler(...)
        switch (GetCSharpCompiler(targetPlatform, buildingForEditor, scriptAssembly))
        {
            case CSharpCompiler.Microsoft:
                return new MicrosoftCSharpCompiler(island, runUpdater);
            default:
                return new CustomCSharpCompiler(island, runUpdater); // MonoCSharpCompiler is replaced with CustomCSharpCompiler
        }
    }
}
