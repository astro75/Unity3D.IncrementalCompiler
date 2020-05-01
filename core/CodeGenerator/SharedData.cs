using System.IO;

public static class SharedData
{
    public const string GeneratedFolder = "generated-by-compiler";

    public static string CompileTimesFileName(string assemblyName) =>
        Path.Combine(GeneratedFolder, $"{assemblyName}.compile-times.txt");
}
