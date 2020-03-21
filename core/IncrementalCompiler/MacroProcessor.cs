using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GenerationAttributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace IncrementalCompiler
{
    public static class MacroProcessor
    {
        struct MacroCtx
        {
            public readonly SemanticModel Model;
            public readonly SyntaxNode Syntax;
            public readonly IOperation Operation;

            public MacroCtx(SemanticModel model, IOperation operation) {
                Model = model;
                Syntax = operation.Syntax;
                Operation = operation;
            }
        }

        delegate (SyntaxNode, SyntaxNode) MacroExecutor(MacroCtx ctx);

        public static CSharpCompilation Run(
            CSharpCompilation compilation, ImmutableArray<SyntaxTree> trees, Dictionary<string, SyntaxTree> sourceMap,
            List<Diagnostic> diagnostic
        )
        {
            var macrosType = typeof(Macros).FullName;
            var macros = compilation.GetTypeByMetadataName(macrosType);

            if (macros == null)
            {
                // skip this step if macros dll is not referenced
                return compilation;
                // throw new Exception($"Could not find type {macrosType} in project.");
            }

            var simpleMethodMacroType = compilation.GetTypeByMetadataName(typeof(SimpleMethodMacro).FullName);
            var varMethodMacroType = compilation.GetTypeByMetadataName(typeof(VarMethodMacro).FullName);
            var allMethods = compilation.GetAllTypes().SelectMany(t => t.GetMembers().OfType<IMethodSymbol>());

            var builder = ImmutableDictionary.CreateBuilder<ISymbol, MacroExecutor>();

            ISymbol macroSymbol(string name) => macros.GetMembers(name).First();

            builder.Add(
                macroSymbol(nameof(Macros.className)),
                ctx =>
                {
                    var enclosingSymbol = ctx.Model.GetEnclosingSymbol(ctx.Syntax.SpanStart);
                    return (ctx.Syntax, enclosingSymbol.ContainingType.ToDisplayString().StringLiteral());
                });

            builder.Add(
                macroSymbol(nameof(Macros.classAndMethodName)),
                ctx =>
                {
                    var enclosingSymbol = ctx.Model.GetEnclosingSymbol(ctx.Syntax.SpanStart);
                    return (ctx.Syntax, enclosingSymbol.ToDisplayString().StringLiteral());
                });

            foreach (var method in allMethods)
            foreach (var attribute in method.GetAttributes())
            {
                if (attribute.AttributeClass == simpleMethodMacroType)
                {
                    CodeGeneration.tryAttribute<SimpleMethodMacro>(
                        attribute, a =>
                        {
                            builder.Add(method, ctx =>
                            {
                                if (ctx.Operation is IInvocationOperation op)
                                {
                                    try {
                                        var sb = new StringBuilder();
                                        sb.Append(a.Pattern);
                                        for (var i = 0; i < op.Arguments.Length; i++)
                                        {
                                            sb.Replace("${expr" + (i + 1) + "}", op.Arguments[i].Syntax.ToString());
                                        }

                                        if (op.Instance != null) sb.Replace("${expr0}", op.Instance.Syntax.ToString());
                                        return (ctx.Syntax, SyntaxFactory.ParseExpression(sb.ToString()));
                                    }
                                    catch (Exception e) {
                                        diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
                                            "ER0001",
                                            "Error",
                                            $"Error for macro {method.Name}: {e.Message}({e.Source}) at {e.StackTrace}",
                                            "Error",
                                            DiagnosticSeverity.Error,
                                            true
                                        ), ctx.Syntax.GetLocation()));
                                    }
                                }
                                return (ctx.Syntax, ctx.Syntax);
                            });
                        }, diagnostic);
                }

                if (attribute.AttributeClass == varMethodMacroType)
                {
                    CodeGeneration.tryAttribute<VarMethodMacro>(
                        attribute, a =>
                        {
                            builder.Add(method, ctx =>
                            {
                                if (ctx.Operation is IInvocationOperation op)
                                {
                                    try
                                    {
                                        var parent = op.Parent?.Parent?.Parent?.Parent;
                                        if (parent is IVariableDeclarationGroupOperation vdg)
                                        {
                                            if (vdg.Declarations.Length != 1) throw new Exception(
                                                $"Expected a single variable declaration"
                                            );
                                            var varDecl = (IVariableDeclaratorOperation) op.Parent.Parent;

                                            var sb = new StringBuilder();
                                            sb.Append(a.Pattern);

                                            for (var i = 0; i < op.Arguments.Length; i++)
                                                sb.Replace("${expr" + (i + 1) + "}", op.Arguments[i].Syntax.ToString());
                                            if (op.Instance != null) sb.Replace("${expr0}", op.Instance.Syntax.ToString());

                                            sb.Replace("${varName}", varDecl.Symbol.ToString());
                                            sb.Replace("${varType}", varDecl.Symbol.Type.Name);

                                            return (vdg.Syntax, SyntaxFactory.ParseStatement(sb.ToString()));
                                        }
                                        else
                                        {
                                            throw new Exception($"Expected {nameof(IVariableDeclarationGroupOperation)}, got {parent?.GetType()}");
                                        }
                                    }
                                    catch (Exception e) {
                                        diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
                                            "ER0001",
                                            "Error",
                                            $"Error for macro {method.Name}: {e.Message}({e.Source}) at {e.StackTrace}",
                                            "Error",
                                            DiagnosticSeverity.Error,
                                            true
                                        ), ctx.Syntax.GetLocation()));
                                    }
                                }
                                return (ctx.Syntax, ctx.Syntax);
                            });
                        }, diagnostic);
                }
            }

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

            var treeEdits = trees.AsParallel().SelectMany(tree =>
            {
//                var walker = new Walker();
                var root = tree.GetCompilationUnitRoot();
                var model = oldCompilation.GetSemanticModel(tree);

                var opFinder = new RootOperationsFinder(model);

                opFinder.Visit(root);

                var changes = new Dictionary<SyntaxNode, SyntaxNode>();

                foreach (var operation in opFinder.results)
                {
                    // Console.WriteLine("Found Operation: " + operation);
                    // Console.WriteLine(operation.Syntax);

                    var descendants = operation.DescendantsAndSelf().ToArray();

                    foreach (var op in descendants.OfType<IPropertyReferenceOperation>()) {
                        if (allMacros.TryGetValue(op.Property, out var fn))
                        {
                            var res = fn(new MacroCtx(model, op));
                            changes.Add(res.Item1, res.Item2);
                        }
                    }

                    foreach (var op in descendants.OfType<IInvocationOperation>()) {
                        if (allMacros.TryGetValue(op.TargetMethod, out var fn)) {
                            var res = fn(new MacroCtx(model, op));
                            changes.Add(res.Item1, res.Item2);
                        }
                    }

                    // foreach (var op in descendants.OfType<IAssignmentOperation>()) {
                    //     if (allMacros.TryGetValue(op.Value, out var fn)) {
                    //         changes.Add(op.Syntax, fn(new MacroCtx(model, op)));
                    //     }
                    // }

                    // foreach (var method in descendants.OfType<IMem>()) {
                    //     if (allMacros.TryGetValue(method., out var fn)) {
                    //         changes.Add(prop.Syntax, fn(model, prop.Syntax));
                    //     }
                    // }

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

                var newRoot = root;

                if (changes.Any())
                {
                    var updatedTree = root.ReplaceNodes(changes.Keys, (a, b) => changes[a]);
//                    Console.WriteLine(updatedTree.GetText());
                    newRoot = updatedTree;
                }

                if (newRoot != root)
                {
                    return new[] {(tree, newRoot)};
                }
                return Enumerable.Empty<(SyntaxTree, CompilationUnitSyntax)>();
            }).ToArray();
            compilation = EditTrees(compilation, sourceMap, treeEdits);
            return compilation;
        }

        public static CSharpCompilation EditTrees(
            CSharpCompilation compilation,
            Dictionary<string, SyntaxTree> sourceMap,
            IEnumerable<(SyntaxTree, CompilationUnitSyntax)> treeEdits
        ) {
            foreach (var (tree, syntax) in treeEdits)
            {
                var originalFilePath = tree.FilePath;
                var editedFilePath = Path.Combine(CodeGeneration.GENERATED_FOLDER, "compile-time", originalFilePath);

                var newTree = tree.WithRootAndOptions(syntax, tree.Options).WithFilePath(editedFilePath);
                sourceMap[tree.FilePath] = newTree;
                compilation = compilation.ReplaceSyntaxTree(tree, newTree);
                Directory.CreateDirectory(Path.GetDirectoryName(editedFilePath));
                File.WriteAllText(editedFilePath, newTree.GetText().ToString());
            }
            return compilation;
        }
    }

    public class RootOperationsFinder : CSharpSyntaxWalker
    {
        private readonly SemanticModel model;
        public List<IOperation> results = new List<IOperation>();

        public RootOperationsFinder(SemanticModel model) {
            this.model = model;
        }

        public override void Visit(SyntaxNode node) {
            var operation = model.GetOperation(node);
            if (operation == null) base.Visit(node);
            else results.Add(operation);
        }
    }

    public class Walker : OperationWalker
    {
        private int ident;
        public override void Visit(IOperation operation) {
            for (var i = 0; i < ident; i++) {
                Console.Write("  ");
            }
            Console.WriteLine(operation?.Kind.ToString());
            ident++;
            base.Visit(operation);
            ident--;
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
