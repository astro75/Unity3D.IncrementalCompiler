using System.Diagnostics;
using System.IO;

internal class IncrementalCompiler : Compiler
{
	public override string Name => "Incremental C# Compiler";
    const string ExeName = "IncrementalCompiler.exe";

    readonly string compilerExePath;

    public IncrementalCompiler(Logger? logger, string directory) : base(logger) {
        compilerExePath = Path.Combine(directory, ExeName);
    }

	protected override Process CreateCompilerProcess(Platform platform, string unityEditorDataDir, string targetProfileDir, string responseFile)
	{
	    if (platform == Platform.Mac)
        {
	        var filename = responseFile.TrimStart('@');
	        var content = File.ReadAllText(filename);
	        content = content.Replace('\'', '\"');
	        File.WriteAllText(filename, content);
	    }

        var processArguments = "-noconfig " + responseFile;
        var process = new Process { StartInfo = CreateOSDependentStartInfo(
            platform, compilerExePath, processArguments, unityEditorDataDir) };
        return process;
	}
}
