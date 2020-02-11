using System.Diagnostics;
using System.IO;

internal class Incremental60Compiler : Compiler
{
	public override string Name => "Incremental C# Compiler 6.0";
    const string DllName = "IncrementalCompiler.dll";


	public Incremental60Compiler(Logger logger, string directory) : base(logger, Path.Combine(directory, "IncrementalCompiler.exe")) { }

	protected override Process CreateCompilerProcess(Platform platform, string unityEditorDataDir, string targetProfileDir, string responseFile)
	{
	    if (platform == Platform.Mac)
        {
	        string filename = responseFile.TrimStart('@');
	        string content = File.ReadAllText(filename);
	        content = content.Replace('\'', '\"');
	        File.WriteAllText(filename, content);
	    }

		string processArguments = compilerPath + "-noconfig " + responseFile;
                                

		var process = new Process();
        process.StartInfo = CreateStartInfo("dotnet", processArguments);
		return process;
	}
}
