using System.Diagnostics;
using System.IO;

internal class Incremental60Compiler : Compiler
{
	public override string Name => "Incremental C# Compiler 6.0";
    const string DllName = "IncrementalCompiler.dll";
    const string ExeName = "IncrementalCompiler.exe";

    readonly string compilerExePath;

    public Incremental60Compiler(Logger logger, string directory) : base(logger) {
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

		// var processArguments = compilerPath + "-noconfig " + responseFile;
		// var process = new Process();
        // process.StartInfo = CreateStartInfo("dotnet", processArguments);
		// return process;

        var processArguments = "-noconfig " + responseFile;
        var process = new Process { StartInfo = CreateStartInfo(compilerExePath, processArguments) };
        return process;
	}
}
