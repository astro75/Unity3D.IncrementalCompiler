using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using GenerationAttributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Semantics;

namespace IncrementalCompiler
{
    public static class MacroProcessor
    {
        public static CSharpCompilation Run(CSharpCompilation compilation, ImmutableArray<SyntaxTree> trees, Dictionary<string, SyntaxTree> sourceMap)
        {
            var macros = compilation.GetTypeByMetadataName(typeof(Macros).FullName);
            var builder = ImmutableDictionary.CreateBuilder<ISymbol, MacroRewriter.PropertyMacro>();

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

            var allMacros = builder.ToImmutable();

//            var namedTypeSymbols = CustomSymbolFinder.GetAllSymbols(compilation);

            /*
            foreach (var typeSymbol in namedTypeSymbols)
            {
                if (typeSymbol == null)
                {
                    Console.WriteLine("null");
                    continue;
                }
                var members = typeSymbol.GetMembers();
                foreach (var member in members)
                {
                    Console.WriteLine(member.Name);
                }
            }
            */

            var oldCompilation = compilation;

            var treeEdits = trees.SelectMany(tree =>
            {
//                var walker = new Walker();
                var root = tree.GetCompilationUnitRoot();
                var model = oldCompilation.GetSemanticModel(tree);

                var changes = new Dictionary<SyntaxNode, SyntaxNode>();

                foreach (var block in root.DescendantNodes(node => node.Kind() != SyntaxKind.Block).OfType<BlockSyntax>())
                {
//                    Console.WriteLine(block.SyntaxTree.FilePath);
                    var op = model.GetOperation(block);
//                    op.Accept(walker);

                    var props = op.DescendantsAndSelf().OfType<IPropertyReferenceExpression>();



                    foreach (var prop in props)
                    {
                        if (allMacros.TryGetValue(prop.Property, out var fn))
                        {
                            changes.Add(prop.Syntax, fn(model, prop.Syntax));
                        }
                    }
                    
//                    Console.WriteLine(op?.Type?.ToString() ?? "null");
//                    var symbol = model.GetDeclaredSymbol(classDecl);
//                    var op = symbol.GetRootOperation();
//                    Console.WriteLine(op?.Type.ToString() ?? "null");
//                    Console.WriteLine(op?.Type);

//                    Console.WriteLine(op?.Type);

//                    var members = symbol.GetMembers();
//                    foreach (var member in members)
//                    {
//
//                        var op = model.GetOperation(member.DeclaringSyntaxReferences[0].GetSyntax().);
//                        Console.WriteLine(op?.Type);
//                    }
                }
                
//                var rewriter = new MacroRewriter(model, builder.ToImmutable());
//                var newRoot = (CompilationUnitSyntax)rewriter.VisitCompilationUnit(root);
//                return rewriter.ChangesMade
//                    ? new[] {(tree, newRoot)}
//                    : Enumerable.Empty<(SyntaxTree, CompilationUnitSyntax)>();

                if (changes.Any())
                {
                    return new[] {(tree, root.ReplaceNodes(changes.Keys, (a, b) => changes[a]))};
                }
                return Enumerable.Empty<(SyntaxTree, CompilationUnitSyntax)>();
            });
            foreach (var (tree, syntax) in treeEdits)
            {
                var newTree = tree.WithRootAndOptions(syntax, tree.Options);
                sourceMap[newTree.FilePath] = newTree;
                compilation = compilation.ReplaceSyntaxTree(
                    tree, newTree);
            }
            return compilation;
        }
    }

    public class Walker : OperationWalker
    {
        private int ident;
        public override void Visit(IOperation operation) {
            for (int i = 0; i < ident; i++)
            {
                Console.Write("  ");
            }
            Console.WriteLine(operation?.Kind.ToString());
            ident++;
            base.Visit(operation);
            ident--;
        }

        public override void VisitBlockStatement(IBlockStatement operation) {
            base.VisitBlockStatement(operation);
        }
    }

    public class CustomSymbolFinder
    {
        public static List<INamedTypeSymbol> GetAllSymbols(Compilation compilation)
        {
            var visitor = new FindAllSymbolsVisitor();
            visitor.Visit(compilation.GlobalNamespace);
            return visitor.AllTypeSymbols;
        }

        private class FindAllSymbolsVisitor : SymbolVisitor
        {
            public List<INamedTypeSymbol> AllTypeSymbols { get; } = new List<INamedTypeSymbol>();

            public override void VisitNamespace(INamespaceSymbol symbol)
            {
                Parallel.ForEach(symbol.GetMembers(), s => s.Accept(this));
            }

            public override void VisitNamedType(INamedTypeSymbol symbol)
            {
                AllTypeSymbols.Add(symbol);
                foreach (var childSymbol in symbol.GetTypeMembers())
                {
                    base.Visit(childSymbol);
                }
            }
        }
    }
}
