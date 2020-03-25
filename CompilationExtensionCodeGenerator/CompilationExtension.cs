using System;
using System.Collections.Generic;
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

        static (IEnumerable<Diagnostic>, CSharpCompilation) processTypeSafe(CSharpCompilation compilation) {
            var mapping = new CodeGeneration.GeneratedFilesMapping();
            var sourceMap = new Dictionary<string, SyntaxTree>();

            compilation = removeGenerated(compilation);

            const bool isUnity = false;

            var (newCompilation, diagnostics) = CodeGeneration.Run(
                isUnity: isUnity,
                incrementalRun: false,
                compilation,
                compilation.SyntaxTrees,
                CSharpParseOptions.Default,
                compilation.AssemblyName ?? "assembly_not_found",
                ref mapping,
                sourceMap
            );

            newCompilation = MacroProcessor.Run(
                isUnity: isUnity,
                newCompilation,
                compilation.SyntaxTrees,
                sourceMap,
                diagnostics
            );

            Console.Out.WriteLine("AAAAAAAAAAAAAAAAAAAAAAAAAA");

            return (diagnostics, newCompilation);
        }

        static CSharpCompilation removeGenerated(CSharpCompilation compilation) =>
            compilation.RemoveSyntaxTrees(compilation.SyntaxTrees.Where(tree =>
                tree.FilePath.Replace("\\", "/").Contains($"/{CodeGeneration.GENERATED_FOLDER}/")
            ));
    }
}
