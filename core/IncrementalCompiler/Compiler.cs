using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using GenerationAttributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using NLog;


namespace IncrementalCompiler
{
    public class CompilationCache
    {
        public CSharpCompilation _compilation;
        public CompileOptions _options;
        public readonly FileTimeList _referenceFileList;
        public readonly FileTimeList _sourceFileList;
        public readonly Dictionary<string, MetadataReference> _referenceMap;
        public readonly Dictionary<string, SyntaxTree> _sourceMap;
        public readonly CodeGeneration.GeneratedFilesMapping _filesMapping;

        public CompilationCache(
            CSharpCompilation compilation,
            CompileOptions options,
            FileTimeList referenceFileList,
            FileTimeList sourceFileList,
            Dictionary<string, MetadataReference> referenceMap,
            Dictionary<string, SyntaxTree> sourceMap, CodeGeneration.GeneratedFilesMapping filesMapping) {
            _referenceFileList = referenceFileList;
            _sourceFileList = sourceFileList;
            _referenceMap = referenceMap;
            _sourceMap = sourceMap;
            _filesMapping = filesMapping;
            _compilation = compilation;
            _options = options;
        }
    }

    public sealed class Compiler : IDisposable
    {
        readonly Logger _logger = LogManager.GetLogger("Compiler");

        readonly string assemblyNameNoExtension;
        readonly CSharpParseOptions parseOptions;

        MemoryStream? _outputDllStream;
        MemoryStream? _outputDebugSymbolStream;
        ImmutableArray<DiagnosticAnalyzer> analyzers;
        const string analyzersPath = "./Analyzers";
        CompileResult? previousResult;
        CompilationCache? _cache;
        bool referencesCompilerAttributes;

        static readonly object _lockAnalyzers = new object();
        static ImmutableArray<DiagnosticAnalyzer>? _loadedAnalyzers;

        void CompileAnalyzer(CompileOptions options, string fullPath, string assembliesPath) {
            _logger.Info($"Compiling analyzer: {fullPath}");
            var name = Path.GetFileNameWithoutExtension(Path.GetFileName(fullPath));
            var parsed = ParseSource(options, fullPath, parseOptions);

            var assemblyRefs =
                AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => MetadataReference.CreateFromFile(a.Location))
                    .ToArray();

            var c = CSharpCompilation.Create(
                name,
                new[] {parsed},
                assemblyRefs,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default,
                    sourceReferenceResolver: new SourceFileResolver(ImmutableArray<string>.Empty, options.WorkDirectory)
                )
            );
            using (var stream = new FileStream(Path.Combine(assembliesPath, name + ".dll"), FileMode.OpenOrCreate))
            {
                var res = c.Emit(stream);
                foreach (var diagnostic in res.Diagnostics)
                {
                    switch (diagnostic.Severity) {
                        case DiagnosticSeverity.Error:
                            _logger.Error(diagnostic.ToString());
                            break;
                        case DiagnosticSeverity.Warning:
                            _logger.Warn(diagnostic.ToString());
                            break;
                        default:
                            _logger.Info(diagnostic.ToString());
                            break;
                    }
                }
                if (!res.Success) throw new Exception($"Could not compile `{fullPath}`");
            }
        }

        string CompileAnalyzers(CompileOptions options) {
            var analyzers = "compiled-analyzers-" + assemblyNameNoExtension;
            var outputPath = Directory.Exists("Temp") ? Path.Combine("Temp", analyzers) : analyzers;

            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
            Directory.CreateDirectory(outputPath);
            var analyzerSources =
                Directory
                    .GetFiles(analyzersPath)
                    .Where(x => x.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            foreach (var cs in analyzerSources)
            {
                CompileAnalyzer(options, cs, outputPath);
            }

            return outputPath;
        }

        class AL : IAnalyzerAssemblyLoader {
            public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);

            public void AddDependencyLocation(string fullPath) { }
        }

        /// <summary>
        /// analyzers can only use dependencies that are already in this project
        /// dependency versions must match those of project dependencies
        /// </summary>
        /// <param name="diagnostics"></param>
        ImmutableArray<DiagnosticAnalyzer> AnalyzersInner(CompileOptions options, List<Diagnostic> diagnostics) {
            // if Location.None is used instead, unity doesnt print the error to console.
            var defaultPos = Location.Create(
                "/Analyzers", TextSpan.FromBounds(0, 0), new LinePositionSpan()
            );

            try {
                if (PlatformHelper.CurrentPlatform == Platform.Mac) return ImmutableArray<DiagnosticAnalyzer>.Empty;

                if (!Directory.Exists(analyzersPath)) {
                    Directory.CreateDirectory(analyzersPath);
                    File.WriteAllText(
                        analyzersPath + "/readme.txt",
                        "Add Roslyn Analyzers here\r\nAdd analyzer dependencies in sub-folders"
                    );
                    return ImmutableArray<DiagnosticAnalyzer>.Empty;
                }

                var loader = new AL();

                var additionalPath = CompileAnalyzers(options);

                var analyzerNames =
                    Directory.GetFiles(analyzersPath).Concat(Directory.GetFiles(additionalPath))
                        .Where(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                var analyzerDependencies =
                    Directory.GetDirectories(analyzersPath).SelectMany(Directory.GetFiles).ToArray();

                foreach (var analyzerDependency in analyzerDependencies)
                {
                    _logger.Info("Analyzer dependency: " + analyzerDependency);
                    loader.LoadFromPath(analyzerDependency);
                }

                _logger.Info("Analyzers:");
                var analyzers =
                    analyzerNames
                    .Select(dll => {
                        _logger.Info("Analyzer dll: " + dll);
                        var _ref = new AnalyzerFileReference(dll, loader);
                        _ref.AnalyzerLoadFailed += (_, e) => {
                            _logger.Error("failed to load analyzer: " + e.TypeName + "; " + e.Message);
                            diagnostics.Add(Diagnostic.Create(new DiagnosticDescriptor(
                                "A01",
                                "Error",
                                "Compiler couldn't load provided code analyzer: " + e.TypeName +
                                ". Please fix or remove from /Analyzers directory. More info in compiler log.",
                                "Error",
                                DiagnosticSeverity.Error,
                                true
                            ), defaultPos));
                        };

                        return _ref.GetAnalyzers(LanguageNames.CSharp);
                    })
                    .Aggregate(new List<DiagnosticAnalyzer>(), (list, a) => {
                        a.ForEach(_logger.Info);
                        list.AddRange(a);
                        return list;
                    })
                    .ToImmutableArray();

                return analyzers;
            } catch (Exception e) {
                _logger.Error(e);
                diagnostics.Add(Diagnostic.Create(new DiagnosticDescriptor(
                    "A02",
                    "Warning",
                    "Exception was thrown when loading analyzers: " + e.Message,
                    "Warning",
                    DiagnosticSeverity.Warning,
                    true
                ), defaultPos));
                return ImmutableArray<DiagnosticAnalyzer>.Empty;
            }
        }

        ImmutableArray<DiagnosticAnalyzer> Analyzers(CompileOptions options, List<Diagnostic> diagnostics) {
            lock (_lockAnalyzers)
            {
                if (_loadedAnalyzers.HasValue) return _loadedAnalyzers.Value;
                _loadedAnalyzers = AnalyzersInner(options, diagnostics);
                return _loadedAnalyzers.Value;
            }
        }

        public Compiler(CompileOptions options) {
            assemblyNameNoExtension = Path.GetFileNameWithoutExtension(options.AssemblyName);
            parseOptions = new CSharpParseOptions(
                LanguageVersion.CSharp8, DocumentationMode.Parse, SourceCodeKind.Regular, options.Defines
            ).WithFeatures(new []{new KeyValuePair<string, string>("IOperation", ""), });
        }

        public CompileResult Build(CompileOptions options)
        {
            var settings = new GenerationSettings(
                partialsFolder: Path.Combine(SharedData.GeneratedFolder, assemblyNameNoExtension),
                macrosFolder: Path.Combine(SharedData.GeneratedFolder, "_macros"),
                txtForPartials: null,
                baseDirectory: ".");

            if (_cache == null ||
                _cache._options.WorkDirectory != options.WorkDirectory ||
                _cache._options.AssemblyName != options.AssemblyName ||
                _cache._options.Output != options.Output ||
                _cache._options.NoWarnings.SequenceEqual(options.NoWarnings) == false ||
                _cache._options.Defines.SequenceEqual(options.Defines) == false ||
                _cache._options.DebugSymbolFile != options.DebugSymbolFile ||
                _cache._options.IsUnityPackage != options.IsUnityPackage)
            {
                (previousResult, _cache) = BuildFull(options, settings);
                return previousResult;
            }
            else
            {
                _cache._options = options;
                return BuildIncremental(options, settings, _cache, previousResult!);
            }
        }

        (CompileResult, CompilationCache) BuildFull(CompileOptions options, GenerationSettings settings)
        {
            var result = new CompileResult();

            var totalSW = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();

            var filesMapping = new CodeGeneration.GeneratedFilesMapping();

            void logTime(string label) {
                var elapsed = sw.Elapsed;
                _logger.Info($"Time elapsed {elapsed} on {label}");
                sw.Restart();
            }

            _logger.Info("BuildFull");

            var referenceFileList = new FileTimeList();
            referenceFileList.Update(options.References);

            var sourceFileList = new FileTimeList();
            sourceFileList.Update(options.Files);

            var referenceMap = options.References.ToDictionary(
                keySelector: file => file,
                elementSelector: s => CreateReference(options, s)
            );
            logTime("Loaded references");

            var sourceMap = options.Files.AsParallel().Select(
                fileName => (fileName, tree: ParseSource(options, fileName, parseOptions))).ToDictionary(t => t.fileName, t => t.tree);
            logTime("Loaded sources");


            var specificDiagnosticOptions = options.NoWarnings.ToDictionary(x => x, _ => ReportDiagnostic.Suppress);
            var compilation = CSharpCompilation.Create(
                options.AssemblyName,
                sourceMap.Values,
                referenceMap.Values,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    // deterministic option fails at runtime:
                    // Unexpected error writing debug information -- 'Unable to load DLL 'Microsoft.DiaSymReader.Native.x86.dll': The specified module could not be found.
                    // deterministic: true,
                    specificDiagnosticOptions: specificDiagnosticOptions,
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default,
                    allowUnsafe: options.Unsafe,
                    // without SourceFileResolver debugging in Rider does not work
                    sourceReferenceResolver: new SourceFileResolver(ImmutableArray<string>.Empty, options.WorkDirectory),
                    optimizationLevel: options.Optimize ? OptimizationLevel.Release : OptimizationLevel.Debug
                )

            );
            logTime("Compilation created");

            var diagnostic = new List<Diagnostic>();

            referencesCompilerAttributes =
                !options.IsUnityPackage
                && compilation.GetTypeByMetadataName(typeof(RecordAttribute).FullName!) != null;

            if (!referencesCompilerAttributes)
            {
                analyzers = ImmutableArray<DiagnosticAnalyzer>.Empty;
            }
            else
            {
                // analyzers = Analyzers(options, diagnostic);
                analyzers = ImmutableArray<DiagnosticAnalyzer>.Empty;
                logTime("Loaded analyzers");

                var ___generatedFiles__unfinished = new List<CodeGeneration.GeneratedCsFile>();

                compilation = CodeGeneration.Run(
                    false,
                    compilation,
                    compilation.SyntaxTrees,
                    parseOptions,
                    assemblyNameNoExtension,
                    filesMapping, sourceMap,
                    settings,
                    ___generatedFiles__unfinished
                ).Tap((compAndDiag) =>
                {
                    diagnostic.AddRange(compAndDiag.Item2);
                    return compAndDiag.Item1;
                });

                logTime("Code generated");

                compilation = MacroProcessor.Run(
                    compilation,
                    compilation.SyntaxTrees,
                    sourceMap,
                    diagnostic,
                    settings,
                    ___generatedFiles__unfinished
                );
                logTime("Macros completed");
            }

            AnalyzeAndEmit(options, result, diagnostic, compilation, analyzers);
            logTime("Emitted dll");

            _logger.Info($"Total elapsed {totalSW.Elapsed}");

            previousResult = result;
            var cache = new CompilationCache(compilation, options, referenceFileList, sourceFileList, referenceMap, sourceMap, filesMapping);
            return (result, cache);
        }

        void AnalyzeAndEmit(
            CompileOptions options,
            CompileResult result,
            ICollection<Diagnostic> diagnostic,
            CSharpCompilation compilation,
            ImmutableArray<DiagnosticAnalyzer> analyzers
        ) {
            diagnostic = diagnostic.Concat(AnalyzersDiagnostics(compilation, analyzers)).ToList();
            Emit(options, result, diagnostic, compilation);
        }

        static ImmutableArray<Diagnostic> AnalyzersDiagnostics(
            CSharpCompilation comp, ImmutableArray<DiagnosticAnalyzer> analyzers
        ) =>
            analyzers.Any()
            ? comp
                .WithAnalyzers(analyzers)
                .GetAnalysisResultAsync(new CancellationToken())
                .Result
                .GetAllDiagnostics()
            : ImmutableArray<Diagnostic>.Empty;

        CompileResult BuildIncremental(
            CompileOptions options, GenerationSettings settings, CompilationCache cache, CompileResult previousResult)
        {
            _logger.Info("BuildIncremental");

            // update reference files

            var referenceChanges = cache._referenceFileList.Update(options.References);
            foreach (var file in referenceChanges.Added)
            {
                _logger.Info("+ {0}", file);
                var reference = CreateReference(options, file);
                cache._compilation = cache._compilation.AddReferences(reference);
                cache._referenceMap.Add(file, reference);
            }
            foreach (var file in referenceChanges.Changed)
            {
                _logger.Info("* {0}", file);
                var reference = CreateReference(options, file);
                cache._compilation = cache._compilation.ReplaceReference(cache._referenceMap[file], reference);
                cache._referenceMap[file] = reference;
            }
            foreach (var file in referenceChanges.Removed)
            {
                _logger.Info("- {0}", file);
                cache._compilation = cache._compilation.RemoveReferences(cache._referenceMap[file]);
                cache._referenceMap.Remove(file);
            }

            // update source files

            var sourceChanges = cache._sourceFileList.Update(options.Files);

            var allTrees = cache._compilation.SyntaxTrees;

            var newTrees = sourceChanges.Added.AsParallel().Select(file => {
                var tree = ParseSource(options, file, parseOptions);
                return (file, tree);
            }).ToArray();

            foreach (var (file, tree) in newTrees) {
                _logger.Info("+ {0}", file);
                cache._sourceMap.Add(file, tree);
            }

            cache._compilation = cache._compilation.AddSyntaxTrees(newTrees.Select(t => t.tree));

            var changes = sourceChanges.Changed.AsParallel().Select(file => {
                var tree = ParseSource(options, file, parseOptions);
                return (file, tree);
            }).ToArray();

            foreach (var (file, tree) in changes) {
                _logger.Info("* {0}", file);
                cache._compilation = cache._compilation.ReplaceSyntaxTree(cache._sourceMap[file], tree);
                cache._sourceMap[file] = tree;
            }

            var removedTrees = sourceChanges.Removed.Select(file =>
            {
                _logger.Info("- {0}", file);
                var tree = cache._sourceMap[file];
                cache._sourceMap.Remove(file);
                return tree;
            }).ToArray();

            var generatedRemove = sourceChanges.Removed.Concat(sourceChanges.Changed).ToArray();
            var generatedFilesRemove = generatedRemove
                .Where(cache._filesMapping.filesDict.ContainsKey)
                .SelectMany(path => cache._filesMapping.filesDict[path])
                .Where(cache._sourceMap.ContainsKey)
                .Select(path => cache._sourceMap[path]);

            cache._compilation = cache._compilation.RemoveSyntaxTrees(removedTrees.Concat(generatedFilesRemove));

            cache._filesMapping.removeFiles(generatedRemove);

            var allAddedTrees = newTrees.Concat(changes).Select(t => t.tree).ToImmutableArray();

            var diagnostic = new List<Diagnostic>();

            if (!referencesCompilerAttributes)
            {

            }
            else
            {
                var ___generatedFiles__unfinished = new List<CodeGeneration.GeneratedCsFile>();

                cache._compilation = CodeGeneration.Run(
                    true, cache._compilation, allAddedTrees, parseOptions, assemblyNameNoExtension,
                    cache._filesMapping, cache._sourceMap, settings,
                    ___generatedFiles__unfinished
                ).Tap(t =>
                {
                    diagnostic.AddRange(t.Item2);
                    return t.Item1;
                });

                //TODO: macros on new generated files


                var treeSet = allAddedTrees.Select(t => t.FilePath).ToImmutableHashSet();
                var treesForMacroProcessor =
                    cache._compilation
                        .SyntaxTrees
                        .Where(t => treeSet.Contains(t.FilePath))
                        .ToImmutableArray();

                cache._compilation = MacroProcessor.Run(
                    cache._compilation,
                    treesForMacroProcessor,
                    cache._sourceMap,
                    diagnostic,
                    settings,
                    ___generatedFiles__unfinished
                );
                // emit or reuse prebuilt output
            }
            diagnostic.AddRange(AnalyzersDiagnostics(cache._compilation, analyzers));

            var reusePrebuilt = previousResult.Succeeded && _outputDllStream != null && (
                (options.PrebuiltOutputReuse == PrebuiltOutputReuseType.WhenNoChange &&
                 sourceChanges.Empty && referenceChanges.Empty) ||
                (options.PrebuiltOutputReuse == PrebuiltOutputReuseType.WhenNoSourceChange &&
                 sourceChanges.Empty && referenceChanges.Added.Count == 0 && referenceChanges.Removed.Count == 0));

            if (reusePrebuilt)
            {
                _logger.Info("Reuse prebuilt output");

                // write dll

                var dllFile = Path.Combine(options.WorkDirectory, options.Output);
                WriteToFile(_outputDllStream!, dllFile);

                // write pdb or mdb

                switch (options.DebugSymbolFile)
                {
                    case DebugSymbolFileType.Pdb:
                        if (_outputDebugSymbolStream != null)
                        {
                            var pdbFile = Path.Combine(
                                options.WorkDirectory,
                                Path.ChangeExtension(options.Output, ".pdb"));
                            WriteToFile(_outputDebugSymbolStream, pdbFile);
                        }
                        break;

                }

                return previousResult;
            }
            else
            {
                _logger.Info("Emit");

                var result = previousResult;
                result.Clear();
                AnalyzeAndEmit(options, result, diagnostic, cache._compilation, analyzers);
                return result;
            }
        }

        private MetadataReference CreateReference(CompileOptions options, string file)
        {
            return MetadataReference.CreateFromFile(Path.Combine(options.WorkDirectory, file));
        }

        private SyntaxTree ParseSource(CompileOptions options, string file, CSharpParseOptions parseOption)
        {
            var fileFullPath = Path.Combine(options.WorkDirectory, file);
            var text = File.ReadAllText(fileFullPath);
            return CSharpSyntaxTree.ParseText(text, parseOption, file, Encoding.UTF8);
        }

        private void Emit(CompileOptions options, CompileResult result, ICollection<Diagnostic> diagnostic, CSharpCompilation compilation)
        {
            _outputDllStream?.Dispose();
            _outputDllStream = new MemoryStream();
            _outputDebugSymbolStream?.Dispose();
            _outputDebugSymbolStream = options.DebugSymbolFile != DebugSymbolFileType.None ? new MemoryStream() : null;

            // emit to memory

            var r = compilation.Emit(
                _outputDllStream,
                _outputDebugSymbolStream,
                options: new EmitOptions(debugInformationFormat:
                    DebugInformationFormat.PortablePdb
                )
            );

            // memory to file

            var dllFile = Path.Combine(options.WorkDirectory, options.Output);
            var pdbFile = Path.Combine(options.WorkDirectory, Path.ChangeExtension(options.Output, ".pdb"));

            // gather result

            var formatter = new DiagnosticFormatter();
            foreach (var d in diagnostic.Concat(r.Diagnostics)) {
                if (d.Severity == DiagnosticSeverity.Warning && !d.IsWarningAsError)
                    result.Warnings.Add(formatter.Format(d, CultureInfo.InvariantCulture));
                else if (d.Severity == DiagnosticSeverity.Error || d.IsWarningAsError)
                    result.Errors.Add(formatter.Format(d, CultureInfo.InvariantCulture));
            }

            result.Succeeded = r.Success;

            if (r.Success)
            {
                WriteToFile(_outputDllStream, dllFile);

                if (_outputDebugSymbolStream != null)
                {
                    var emitDebugSymbolFile = pdbFile;
                    WriteToFile(_outputDebugSymbolStream, emitDebugSymbolFile);
                }
            }
        }

        void WriteToFile(MemoryStream stream, string file) {
            _logger.Info($"Writing data to file {file}, size: {stream.Length:N0}");
            using (var dllStream = new FileStream(file, FileMode.Create))
            {
                stream.Seek(0L, SeekOrigin.Begin);
                stream.CopyTo(dllStream);
            }
        }

        public void Dispose() {
            _outputDllStream?.Dispose();
            _outputDebugSymbolStream?.Dispose();
        }
    }
}
