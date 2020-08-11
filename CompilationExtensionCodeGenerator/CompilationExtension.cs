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

            var generatedFiles = new List<CodeGeneration.GeneratedCsFile>();

            var parseOptions =
                compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ?? CSharpParseOptions.Default;

            var sw = Stopwatch.StartNew();

            var treesBefore = compilation.SyntaxTrees;

            var (newCompilation, diagnostics) = CodeGeneration.Run(
                incrementalRun: false,
                compilation,
                compilation.SyntaxTrees,
                parseOptions,
                assemblyName,
                mapping,
                sourceMap,
                settings,
                generatedFiles
            );

            debugPrint($"Code generation: {sw.Elapsed}");
            sw.Restart();

            var sw2 = Stopwatch.StartNew();

            newCompilation = MacroProcessor.Run(
                newCompilation,
                treesBefore,
                sourceMap,
                diagnostics,
                settings,
                generatedFiles,
                logTime: label => {
                  debugPrint($"Macro processor {label}: {sw2.Elapsed}");
                  sw2.Restart();
                }
            );

            debugPrint($"Macro processor: {sw.Elapsed}");

            {
                if (Directory.Exists(generatedForAssembly))
                {
                    // windows explorer likes to lock folders, so delete files only
                    CodeGeneration.DeleteFilesRecursively(generatedForAssembly);
                }
                Directory.CreateDirectory(settings.partialsFolder);

                foreach (var generatedFile in generatedFiles)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(generatedFile.FullPath));
                        if (File.Exists(generatedFile.FullPath))
                        {
                            diagnostics.Add(Diagnostic.Create(new DiagnosticDescriptor(
                                "ER0002", "Error", $"Could not generate file '{generatedFile.FullPath}'. File already exists.", "Error", DiagnosticSeverity.Error, true
                            ), generatedFile.Location));
                        }
                        else
                        {
                            File.WriteAllText(generatedFile.FullPath, generatedFile.Contents);
                        }
                    }
                    catch (Exception e)
                    {
                        diagnostics.Add(Diagnostic.Create(new DiagnosticDescriptor(
                            "ER0003", "Error", $"Could not generate file '{generatedFile.FullPath}'. {e.Message}.", "Error", DiagnosticSeverity.Error, true
                        ), generatedFile.Location));
                    }
                }
                {
                    var targetsFileName = generatedForAssembly + ".targets";
                    File.WriteAllText(targetsFileName, generateTargetsXml(settings, generatedFiles));
                }
            }

            /*var generatedPath = file.FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(generatedPath));
                if (File.Exists(generatedPath))
                {
                    diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
                        "ER0002", "Error", $"Could not generate file '{generatedPath}'. File already exists.", "Error", DiagnosticSeverity.Error, true
                    ), file.Location));
                }
                else
                {
                    File.WriteAllText(generatedPath, file.Contents);
                }*/

            return (diagnostics, newCompilation);
        }

        static void debugPrint(string message) {
            //Console.Out.WriteLine(message);
        }

        static CSharpCompilation removeGenerated(CSharpCompilation compilation) =>
            compilation.RemoveSyntaxTrees(compilation.SyntaxTrees.Where(tree =>
                tree.FilePath.Replace("\\", "/").Contains($"/{SharedData.GeneratedFolder}/")
            ));

        static string generateTargetsXml(GenerationSettings settings, IEnumerable<CodeGeneration.GeneratedCsFile> files) {
            var str = string.Join("\n", files.Select(f =>
            {
                var relativeToWorkingDir = settings.getRelativePath(f.FullPath);
                var type = f.TransformedFile ? "None" : "Compile";
                return $"  <{type} Include=\"{relativeToWorkingDir}\" Link=\"{f.RelativePath}\" />";
            }).ToArray());
            return targetsXml(str);
        }

        // no space at the beginning is allowed
        static string targetsXml(string itemGroupContent) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""4.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">

<ItemGroup>
{itemGroupContent}
</ItemGroup>

</Project>
";
    }
}
