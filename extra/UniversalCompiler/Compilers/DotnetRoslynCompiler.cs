using System.Diagnostics;
using System.IO;

internal class DotnetRoslynCompiler : Compiler
{
	public override string Name => "Roslyn Compiler";
    public const string ExeName = "csc.dll";

    readonly string compilerExePath;

    public DotnetRoslynCompiler(Logger? logger, string directory) : base(logger) {
        compilerExePath = Path.Combine(directory, ExeName);
    }

	protected override Process CreateCompilerProcess(
        Platform platform, string unityEditorDataDir, string responseFile)
	{
        var processArguments = $"{compilerExePath} -shared -noconfig {responseFile}";
        var process = new Process { StartInfo = CreateStartInfo("dotnet", processArguments) };
        return process;
	}
}
