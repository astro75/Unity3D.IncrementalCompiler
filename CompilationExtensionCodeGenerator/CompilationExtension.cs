using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CompilationExtensionInterfaces;
using IncrementalCompiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CompilationExtensionCodeGenerator {

    // ReSharper disable once UnusedType.Global
    public class CompilationExtension : IProcessCompilation {
        public IEnumerable<object> process(ref object compilation, string baseDirectory) {
            // processTypeSafe((CSharpCompilation) compilation, baseDirectory);
            var (diagnostics, resultCompilation) = processTypeSafe((CSharpCompilation) compilation, baseDirectory);
            compilation = resultCompilation;
            return diagnostics;
        }

        static (IEnumerable<Diagnostic>, CSharpCompilation) processTypeSafe(
            CSharpCompilation compilation, string baseDirectory
        ) {
            var mapping = new CodeGeneration.GeneratedFilesMapping();
            var sourceMap = new Dictionary<string, SyntaxTree>();

            compilation = removeGenerated(compilation);

            var assemblyName = compilation.AssemblyName ?? "assembly_not_found";

            var generatedBase = Path.Combine(baseDirectory, SharedData.GeneratedFolder);
            var generatedForAssembly = Path.Combine(generatedBase, assemblyName);

            var settings = new GenerationSettings(
                partialsFolder: Path.Combine(generatedForAssembly, "partials"),
                macrosFolder: Path.Combine(generatedForAssembly, "macros"),
                txtForPartials: null,
                baseDirectory: baseDirectory);

            {
                if (Directory.Exists(generatedForAssembly))
                {
                    // windows explorer likes to lock folders, so delete files only
                    CodeGeneration.DeleteFilesRecursively(generatedForAssembly);
                }
                Directory.CreateDirectory(settings.partialsFolder);
            }

            var parseOptions =
                compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ?? CSharpParseOptions.Default;

            var sw = Stopwatch.StartNew();

            var (newCompilation, diagnostics) = CodeGeneration.Run(
                incrementalRun: false,
                compilation,
                compilation.SyntaxTrees,
                parseOptions,
                assemblyName,
                mapping,
                sourceMap,
                settings
            );

            debugPrint($"Code generation: {sw.Elapsed}");
            sw.Restart();

            newCompilation = MacroProcessor.Run(
                newCompilation,
                compilation.SyntaxTrees,
                sourceMap,
                diagnostics,
                settings
            );

            debugPrint($"Macro processor: {sw.Elapsed}");

            return (diagnostics, newCompilation);
        }

        static void debugPrint(string message) {
            // Console.Out.WriteLine(message);
        }

        static CSharpCompilation removeGenerated(CSharpCompilation compilation) =>
            compilation.RemoveSyntaxTrees(compilation.SyntaxTrees.Where(tree =>
                tree.FilePath.Replace("\\", "/").Contains($"/{SharedData.GeneratedFolder}/")
            ));
    }
}
