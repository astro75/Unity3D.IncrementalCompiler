using System.Collections.Immutable;
using System.Linq;
using GenerationAttributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IncrementalCompiler
{
    public static class MacroProcessor
    {
        public static CSharpCompilation Run(CSharpCompilation compilation, ImmutableArray<SyntaxTree> trees)
        {
            var macros = compilation.GetTypeByMetadataName(typeof(Macros).FullName);
            var builder = ImmutableDictionary.CreateBuilder<ISymbol, MacroRewriter.MemberAccess>();

            ISymbol macroSymbol(string name) => macros.GetMembers(name).First();

            builder.Add(
                macroSymbol(nameof(Macros.className)),
                (model, syntax) =>
                {
                    var enclosingSymbol = model.GetEnclosingSymbol(syntax.SpanStart);
                    return enclosingSymbol.ContainingType.ToDisplayString().StringLiteral();
                });

            builder.Add(
                macroSymbol(nameof(Macros.classAndMethodName)),
                (model, syntax) =>
                {
                    var enclosingSymbol = model.GetEnclosingSymbol(syntax.SpanStart);
                    return enclosingSymbol.ToDisplayString().StringLiteral();
                });

            var treeEdits = trees.AsParallel().SelectMany(tree =>
            {
                var root = tree.GetCompilationUnitRoot();
                var model = compilation.GetSemanticModel(tree);
                var rewriter = new MacroRewriter(model, builder.ToImmutable());
                var newRoot = (CompilationUnitSyntax)rewriter.VisitCompilationUnit(root);
                return rewriter.ChangesMade
                    ? new[] {(tree, newRoot)}
                    : Enumerable.Empty<(SyntaxTree, CompilationUnitSyntax)>();
            });
            foreach (var (tree, syntax) in treeEdits)
            {
                compilation = compilation.ReplaceSyntaxTree(
                    tree, tree.WithRootAndOptions(syntax, tree.Options));
            }
            return compilation;
        }
    }
}
