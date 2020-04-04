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
        public class MacroCtx
        {
            public readonly SemanticModel Model;
            public readonly Dictionary<SyntaxNode, SyntaxNode> changedNodes =
                new Dictionary<SyntaxNode, SyntaxNode>();
            public readonly Dictionary<SyntaxNode, SyntaxList<StatementSyntax>> changedStatements =
                new Dictionary<SyntaxNode, SyntaxList<StatementSyntax>>();

            public MacroCtx(SemanticModel model) {
                Model = model;
            }
        }

        public static CSharpCompilation Run(
            CSharpCompilation compilation, ImmutableArray<SyntaxTree> trees, Dictionary<string, SyntaxTree> sourceMap,
            List<Diagnostic> diagnostic, GenerationSettings settings
        )
        {
            var macrosType = typeof(Macros).FullName!;
            var macros = compilation.GetTypeByMetadataName(macrosType);

            if (macros == null)
            {
                // skip this step if macros dll is not referenced
                return compilation;
            }

            var simpleMethodMacroType = compilation.GetTypeByMetadataName(typeof(SimpleMethodMacro).FullName!);
            var statementMethodMacroType = compilation.GetTypeByMetadataName(typeof(StatementMethodMacro).FullName!);
            var varMethodMacroType = compilation.GetTypeByMetadataName(typeof(VarMethodMacro).FullName!);
            var allMethods = compilation.GetAllTypes().SelectMany(t => t.GetMembers().OfType<IMethodSymbol>());

            var builderExpressions = ImmutableDictionary.CreateBuilder<ISymbol, Action<MacroCtx, IOperation>>();
            var builderStatements = ImmutableDictionary.CreateBuilder<ISymbol, Action<MacroCtx, IOperation>>();

            ISymbol macroSymbol(string name) => macros.GetMembers(name).First();

            builderExpressions.Add(
                macroSymbol(nameof(Macros.className)),
                (ctx, op) =>
                {
                    var enclosingSymbol = ctx.Model.GetEnclosingSymbol(op.Syntax.SpanStart);
                    ctx.changedNodes.Add(op.Syntax, enclosingSymbol.ContainingType.ToDisplayString().StringLiteral());
                });

            builderExpressions.Add(
                macroSymbol(nameof(Macros.classAndMethodName)),
                (ctx, op) =>
                {
                    var enclosingSymbol = ctx.Model.GetEnclosingSymbol(op.Syntax.SpanStart);
                    ctx.changedNodes.Add(op.Syntax, enclosingSymbol.ToDisplayString().StringLiteral());
                });

            void tryMacro(IOperation op, IMethodSymbol method, Action act) {
                try
                {
                    act();
                }
                catch (Exception e)
                {
                    diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
                        "ER0001",
                        "Error",
                        $"Error for macro {method.Name}: {e.Message}({e.Source}) at {e.StackTrace}",
                        "Error",
                        DiagnosticSeverity.Error,
                        true
                    ), op.Syntax.GetLocation()));
                }
            }

            void replaceArguments(StringBuilder sb, IInvocationOperation iop) {
                for (var i = 0; i < iop.Arguments.Length; i++)
                {
                    var arg = iop.Arguments[i];

                    string expr;
                    if (arg.ArgumentKind == ArgumentKind.DefaultValue)
                    {
                        expr = defaultValueToString(arg.Value);

                        string defaultValueToString(IOperation val) {
                            switch (val)
                            {
                                case ILiteralOperation literalOp:
                                    if (literalOp.ConstantValue.HasValue)
                                    {
                                        return literalOp.ConstantValue.Value?.ToString() ?? "null";
                                    }
                                    else throw new Exception("Literal constant has no value");
                                case IConversionOperation conversionOp:
                                    // enums
                                    return $"(({conversionOp.Type.ToDisplayString()}) {defaultValueToString(conversionOp.Operand)})";
                                case IDefaultValueOperation defaultValueOp:
                                    return $"default({defaultValueOp.Type.ToDisplayString()})";
                                default:
                                    throw new Exception(
                                        $"Expected '{arg.Parameter.Name}' to be of type " +
                                        $"{nameof(ILiteralOperation)}, but got {arg.Value.GetType()}");
                            }
                        }
                    }
                    else
                    {
                        expr = arg.Syntax.ToString();
                    }
                    sb.Replace("${" + arg.Parameter.Name + "}", expr);
                    sb.Replace("${expr" + (i) + "}", expr);
                }
                if (iop.Instance != null) sb.Replace("${this}", iop.Instance.Syntax.ToString());
            }

            foreach (var method in allMethods)
            foreach (var attribute in method.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, simpleMethodMacroType))
                {
                    CodeGeneration.tryAttribute<SimpleMethodMacro>(
                        attribute, a =>
                        {
                            builderExpressions.Add(method, (ctx, op) =>
                            {
                                if (op is IInvocationOperation iop)
                                {
                                    tryMacro(op, method, () =>
                                    {
                                        var sb = new StringBuilder();
                                        sb.Append(a.pattern);
                                        replaceArguments(sb, iop);
                                        ctx.changedNodes.Add(iop.Syntax, SyntaxFactory.ParseExpression(sb.ToString()));
                                    });
                                }
                            });
                        }, diagnostic);
                }

                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, statementMethodMacroType))
                {
                    CodeGeneration.tryAttribute<StatementMethodMacro>(
                        attribute, a =>
                        {
                            builderExpressions.Add(method, (ctx, op) =>
                            {
                                if (op is IInvocationOperation iop)
                                {
                                    tryMacro(op, method, () =>
                                    {
                                        var parent = op.Parent;
                                        if (parent is IExpressionStatementOperation statementOperation)
                                        {
                                            var sb = new StringBuilder();
                                            sb.Append("{");
                                            sb.Append(a.pattern);
                                            sb.Append("}");

                                            replaceArguments(sb, iop);

                                            var parsedBlock = (BlockSyntax) SyntaxFactory.ParseStatement(sb.ToString());
                                            ctx.changedStatements.Add(statementOperation.Syntax, parsedBlock.Statements);
                                        }
                                        else
                                        {
                                            throw new Exception($"Expected {nameof(IExpressionStatementOperation)}, got {parent?.GetType()}");
                                        }
                                    });
                                }
                            });
                        }, diagnostic);
                }

                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, varMethodMacroType))
                {
                    CodeGeneration.tryAttribute<VarMethodMacro>(
                        attribute, a =>
                        {
                            builderStatements.Add(method, (ctx, op) =>
                            {
                                if (op is IInvocationOperation iop)
                                {
                                    tryMacro(op, method, () =>
                                    {
                                        var parent4 = op.Parent?.Parent?.Parent?.Parent;
                                        if (parent4 is IVariableDeclarationGroupOperation vdgop)
                                        {
                                            if (vdgop.Declarations.Length != 1) throw new Exception(
                                                $"Expected a single variable declaration"
                                            );
                                            var varDecl = (IVariableDeclaratorOperation) op.Parent!.Parent!;

                                            var sb = new StringBuilder();
                                            sb.Append("{");
                                            sb.Append(a.pattern);
                                            sb.Append("}");

                                            replaceArguments(sb, iop);

                                            sb.Replace("${varName}", varDecl.Symbol.ToString());
                                            sb.Replace("${varType}", varDecl.Symbol.Type.ToDisplayString());

                                            var parsedBlock = (BlockSyntax) SyntaxFactory.ParseStatement(sb.ToString());
                                            ctx.changedStatements.Add(vdgop.Syntax, parsedBlock.Statements);
                                        }
                                        else
                                        {
                                            throw new Exception($"Expected {nameof(IVariableDeclarationGroupOperation)}, got {parent4?.GetType()}");
                                        }
                                    });
                                }
                            });
                        }, diagnostic);
                }
            }

            var macrosExpressions = builderExpressions.ToImmutable();
            var macrosStatements = builderStatements.ToImmutable();

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

                var ctx = new MacroCtx(model);

                foreach (var operation in opFinder.results)
                {
                    // Console.WriteLine("Found Operation: " + operation);
                    // Console.WriteLine(operation.Syntax);

                    var descendants = operation.DescendantsAndSelf().ToArray();

                    foreach (var op in descendants.OfType<IPropertyReferenceOperation>())
                    {
                        if (macrosExpressions.TryGetValue(op.Property.OriginalDefinition, out var act)) act(ctx, op);
                    }

                    foreach (var op in descendants.OfType<IInvocationOperation>())
                    {
                        var method = op.TargetMethod.OriginalDefinition;
                        { if (macrosExpressions.TryGetValue(method, out var act)) act(ctx, op); }
                        { if (macrosStatements.TryGetValue(method, out var act)) act(ctx, op); }
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

                var newRoot = root;

                if (ctx.changedNodes.Any() || ctx.changedStatements.Any())
                {
                    var replacer = new MacroReplacer(ctx);
                    var updatedTree = (CompilationUnitSyntax) replacer.Visit(root);
                    // var updatedTree = root.ReplaceNodes(changes.Keys, (a, b) => changes[a]);
                    // Console.WriteLine(updatedTree.GetText());
                    newRoot = updatedTree;
                }

                if (newRoot != root)
                {
                    // TODO: do not normalize whitespace for the whole file
                    // need to fix whitespace in MacroReplacer first
                    return new[] {(tree, newRoot.NormalizeWhitespace())};
                }
                return Enumerable.Empty<(SyntaxTree, CompilationUnitSyntax)>();
            }).ToArray();
            compilation = EditTrees(compilation, sourceMap, treeEdits, settings);
            return compilation;
        }

        public static CSharpCompilation EditTrees(
            CSharpCompilation compilation,
            Dictionary<string, SyntaxTree> sourceMap,
            IEnumerable<(SyntaxTree, CompilationUnitSyntax)> treeEdits,
            GenerationSettings settings
        ) {
            foreach (var (tree, syntax) in treeEdits)
            {
                var originalFilePath = settings.getRelativePath(tree.FilePath);
                var editedFilePath = Path.Combine(settings.macrosFolder, originalFilePath);

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

        public override void Visit(SyntaxNode? node) {
            if (node == null) return;
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
