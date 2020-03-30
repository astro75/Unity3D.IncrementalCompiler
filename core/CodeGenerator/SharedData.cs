namespace IncrementalCompiler
{
    public static class SharedData
    {
        public const string GeneratedFolder = "generated-by-compiler";
        public static string GeneratedFilesListTxt(string assemblyName) => $"Generated-files-{assemblyName}.txt";
    }
}
