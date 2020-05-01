using System.Diagnostics;
using System.IO;

internal class RoslynCompiler : Compiler
{
	public override string Name => "Roslyn Compiler";
    public const string ExeName = "csc.exe";

    readonly string compilerExePath;

    public RoslynCompiler(Logger? logger, string directory) : base(logger) {
        compilerExePath = Path.Combine(directory, ExeName);
    }

	protected override Process CreateCompilerProcess(
        Platform platform, string unityEditorDataDir, string responseFile)
	{
        var processArguments = "-shared -noconfig " + responseFile;
        var process = new Process { StartInfo = CreateOSDependentStartInfo(
            platform, compilerExePath, processArguments, unityEditorDataDir) };
        return process;
	}
}
