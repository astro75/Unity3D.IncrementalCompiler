namespace IncrementalCompiler
{
    public static class SharedData
    {
        public const string GeneratedFolder = "Generated";
        public static string GeneratedFilesListTxt(string assemblyName) => $"Generated-files-{assemblyName}.txt";
    }
}
