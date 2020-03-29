using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CompilationExtensionInterfaces;
using IncrementalCompiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CompilationExtensionCodeGenerator {

    // ReSharper disable once UnusedType.Global
    public class CompilationExtension : IProcessCompilation {
        public IEnumerable<object> process(ref object compilation) {
            var (diagnostics, resultCompilation) = processTypeSafe((CSharpCompilation) compilation);
            compilation = resultCompilation;
            return diagnostics;
        }

        public const string GENERATED_FOLDER = "generated-by-compiler";

        static (IEnumerable<Diagnostic>, CSharpCompilation) processTypeSafe(CSharpCompilation compilation) {
            var mapping = new CodeGeneration.GeneratedFilesMapping();
            var sourceMap = new Dictionary<string, SyntaxTree>();

            compilation = removeGenerated(compilation);

            var settings = new GenerationSettings(
                partialsFolder: Path.Combine(GENERATED_FOLDER, "partials"),
                macrosFolder: Path.Combine(GENERATED_FOLDER, "macros"),
                txtForPartials: null);

            var (newCompilation, diagnostics) = CodeGeneration.Run(
                incrementalRun: false,
                compilation,
                compilation.SyntaxTrees,
                CSharpParseOptions.Default,
                compilation.AssemblyName ?? "assembly_not_found",
                mapping,
                sourceMap,
                settings
            );

            newCompilation = MacroProcessor.Run(
                newCompilation,
                compilation.SyntaxTrees,
                sourceMap,
                diagnostics,
                settings
            );

            return (diagnostics, newCompilation);
        }

        static CSharpCompilation removeGenerated(CSharpCompilation compilation) =>
            compilation.RemoveSyntaxTrees(compilation.SyntaxTrees.Where(tree =>
                tree.FilePath.Replace("\\", "/").Contains($"/{GENERATED_FOLDER}/")
            ));
    }
}
