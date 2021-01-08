﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenerationAttributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IncrementalCompiler {
  public class MacroHelper {
    public readonly IAssemblySymbol macrosAssembly;
    readonly List<Diagnostic> diagnostic;
    public readonly ImmutableArray<RootOperationsFinder> operations;
    public readonly IMethodSymbol[] allMethods;
    public readonly ImmutableDictionary<ISymbol, Action<MacroProcessor.MacroCtx, IOperation>>.Builder builderInvocations =
      ImmutableDictionary.CreateBuilder<ISymbol, Action<MacroProcessor.MacroCtx, IOperation>>();
    public readonly ImmutableDictionary<IMethodSymbol, Action<MacroProcessor.MacroCtx>>.Builder builderDefinitions =
      ImmutableDictionary.CreateBuilder<IMethodSymbol, Action<MacroProcessor.MacroCtx>>();

    public MacroHelper(IAssemblySymbol macrosAssembly, List<Diagnostic> diagnostic,
      IAssemblySymbol[] referencedAssemblies, ImmutableArray<SyntaxTree> trees, CSharpCompilation compilation) {
      this.macrosAssembly = macrosAssembly;
      this.diagnostic = diagnostic;

      var typesToCheck = new List<INamedTypeSymbol>();

      {
        var typesWithMacroAttributesType = getTypeSymbol<TypesWithMacroAttributes>();

        foreach (var assembly in referencedAssemblies) collectTypesForMacros(assembly);

        void collectTypesForMacros(IAssemblySymbol assembly) {
          foreach (var attr in assembly.GetAttributes()) {
            var c = attr.AttributeClass;
            if (c != null && SymbolEqualityComparer.Default.Equals(c, typesWithMacroAttributesType)) {
              var s = attr.ConstructorArguments[0].Values.Select(_ => {
                var symbol = (INamedTypeSymbol?) _.Value;
                if (symbol == null) throw new Exception();
                return symbol;
              });
              typesToCheck.AddRange(s);
            }
          }
        }
      }

      allMethods = typesToCheck
        .SelectMany(_ => _.ConstructedFrom.GetMembers().OfType<IMethodSymbol>())
        .Select(_ => _.OriginalDefinition)
        .ToArray();

      {
        var bag = new ConcurrentBag<RootOperationsFinder>();
        trees.AsParallel().ForAll(tree => {
          var root = tree.GetCompilationUnitRoot();
          var model = compilation.GetSemanticModel(tree);
          var opFinder = new RootOperationsFinder(model, tree);
          opFinder.Visit(root);
          bag.Add(opFinder);
        });
        operations = bag.ToImmutableArray();
      }
    }

    public INamedTypeSymbol getTypeSymbol<T>() =>
      macrosAssembly.GetTypeByMetadataName(typeof(T).FullName!)!;

    public void tryMacro(IOperation op, IMethodSymbol method, Action act) =>
      tryMacro(op.Syntax, method, act);

    public void tryMacro(SyntaxNode location, IMethodSymbol method, Action act) {
      try {
        act();
      }
      catch (Exception e) {
        var expectedException = e is MacroProcessorError;
        var message = expectedException
          ? e.Message
          : $"Error for macro {method.Name}: {e.Message}. ({e.Source}) at {e.StackTrace}";
        addError(location, message);
      }
    }

    public void addError(SyntaxNode location, string message) {
      diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
        "ER0001",
        "Error",
        message,
        "Error",
        DiagnosticSeverity.Error,
        true
      ), location.GetLocation()));
    }
  }

  public static class MacroProcessor {
    public static CSharpCompilation Run(
      CSharpCompilation compilation, ImmutableArray<SyntaxTree> trees, Dictionary<string, SyntaxTree> sourceMap,
      List<Diagnostic> diagnostic, GenerationSettings settings, List<CodeGeneration.GeneratedCsFile> generatedFiles,
      Action<string>? logTime = null
    ) {
      // return compilation;

      var referencedAssemblies = compilation.Assembly.GetReferencedAssembliesAndSelf();

      var maybeMacrosAssembly = referencedAssemblies.FirstOrDefault(_ => _.Name == "Macros");

      if (maybeMacrosAssembly == null) // skip this step if macros dll is not referenced
        return compilation;

      var macrosAssembly = maybeMacrosAssembly;

      // GetTypeByMetadataName searches in assembly and its direct references only
      var macrosClass = macrosAssembly.GetTypeByMetadataName(typeof(Macros).FullName!);

      if (macrosClass == null) {
        diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
          "ER0003", "Error", "Macros.dll assembly must be referenced directly.", "Error", DiagnosticSeverity.Error, true
        ), compilation.Assembly.Locations[0]));
        return compilation;
      }

      // var ss = Stopwatch.StartNew();
      // compilation.Emit(new MemoryStream());
      // Console.Out.WriteLine("ss " + ss.Elapsed);

      var helper = new MacroHelper(macrosAssembly, diagnostic, referencedAssemblies, trees, compilation);

      var simpleMethodMacroType = helper.getTypeSymbol<SimpleMethodMacro>();
      var statementMethodMacroType = helper.getTypeSymbol<StatementMethodMacro>();
      var varMethodMacroType = helper.getTypeSymbol<VarMethodMacro>();
#pragma warning disable 618
      var inlineType = helper.getTypeSymbol<Inline>();
#pragma warning restore 618
      var lazyPropertyType = helper.getTypeSymbol<LazyProperty>();

      logTime?.Invoke("a1");

      ISymbol macroSymbol(string name) => macrosClass.GetMembers(name).First();

      helper.builderInvocations.Add(
        macroSymbol(nameof(Macros.className)),
        (ctx, op) => {
          var enclosingSymbol = ctx.Model.GetEnclosingSymbol(op.Syntax.SpanStart);
          ctx.ChangedNodes.Add(op.Syntax, enclosingSymbol!.ContainingType.ToDisplayString().StringLiteral());
        });

      helper.builderInvocations.Add(
        macroSymbol(nameof(Macros.classAndMethodName)),
        (ctx, op) => {
          var enclosingSymbol = ctx.Model.GetEnclosingSymbol(op.Syntax.SpanStart);
          ctx.ChangedNodes.Add(op.Syntax, enclosingSymbol!.ToDisplayString().StringLiteral());
        });

      void replaceArguments(MacroCtx ctx, StringBuilder sb, IInvocationOperation iop) {
        for (var i = 0; i < iop.Arguments.Length; i++) {
          var arg = iop.Arguments[i];

          string expr;
          if (arg.ArgumentKind == ArgumentKind.DefaultValue) {
            expr = defaultValueToString(arg.Value);

            string defaultValueToString(IOperation val) {
              switch (val) {
                case ILiteralOperation literalOp:
                  if (literalOp.ConstantValue.HasValue)
                    return literalOp.ConstantValue.Value?.ToString() ?? "null";
                  else throw new Exception("Literal constant has no value");
                case IConversionOperation conversionOp when conversionOp.Type != null:
                  // enums
                  return $"(({conversionOp.Type.ToDisplayString()}) {defaultValueToString(conversionOp.Operand)})";
                case IDefaultValueOperation defaultValueOp when defaultValueOp.Type != null:
                  return $"default({defaultValueOp.Type.ToDisplayString()})";
                default:
                  throw new Exception(
                    $"Expected '{arg.Parameter?.Name}' to be of type " +
                    $"{nameof(ILiteralOperation)}, but got {arg.Value.GetType()}");
              }
            }
          }
          else {
            var syntax = ctx.Visit(arg.Syntax);
            if (syntax is ArgumentSyntax argSyntax) {
              // remove explicit argument name form result
              // Eg. someMacroMethod(argumentName: expression)
              // we only want expression
              syntax = argSyntax.WithNameColon(null);
            }
            expr = syntax.ToString();
          }

          if (arg.Parameter != null) sb.Replace("${" + arg.Parameter.Name + "}", expr);
          sb.Replace("${expr" + i + "}", expr);
        }

        if (iop.Instance != null) sb.Replace("${this}", iop.Instance.Syntax.ToString());
      }

      var implicits = new MacroProcessorImplicits(helper);

      logTime?.Invoke("a2");

      foreach (var method in helper.allMethods)
      foreach (var attribute in method.GetAttributes()) {
        implicits.CheckMethodAttribute(method, attribute);

        if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, simpleMethodMacroType))
          CodeGeneration.tryAttribute<SimpleMethodMacro>(
            attribute, a => {
              helper.builderInvocations.Add(method, (ctx, op) => {
                if (op is IInvocationOperation iop)
                  helper.tryMacro(op, method, () => {
                    var sb = ctx.EmptyStringBuilder();
                    sb.Append(a.Pattern);
                    replaceArguments(ctx, sb, iop);
                    ctx.ChangedNodes.Add(iop.Syntax, SyntaxFactory.ParseExpression(sb.ToString()));
                  });
              });
            }, diagnostic);

        if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, statementMethodMacroType))
          CodeGeneration.tryAttribute<StatementMethodMacro>(
            attribute, a => {
              helper.builderInvocations.Add(method, (ctx, op) => {
                if (op is IInvocationOperation iop)
                  helper.tryMacro(op, method, () => {
                    var parent = op.Parent;
                    if (parent is IExpressionStatementOperation statementOperation) {
                      var sb = ctx.EmptyStringBuilder();
                      sb.Append("{");
                      sb.Append(a.Pattern);
                      sb.Append("}");

                      replaceArguments(ctx, sb, iop);

                      var parsedBlock = (BlockSyntax) SyntaxFactory.ParseStatement(sb.ToString());
                      ctx.ChangedStatements.Add(statementOperation.Syntax, parsedBlock.Statements.ToArray<SyntaxNode>());
                    }
                    else {
                      throw new Exception($"Expected {nameof(IExpressionStatementOperation)}, got {parent?.GetType()}");
                    }
                  });
              });
            }, diagnostic);

        if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, varMethodMacroType))
          CodeGeneration.tryAttribute<VarMethodMacro>(
            attribute, a => {
              helper.builderInvocations.Add(method, (ctx, op) => {
                if (op is IInvocationOperation iop)
                  helper.tryMacro(op, method, () => {
                    var parent4 = op.Parent?.Parent?.Parent?.Parent;
                    if (parent4 is IVariableDeclarationGroupOperation vdgop) {
                      if (vdgop.Declarations.Length != 1)
                        throw new Exception(
                          "Expected a single variable declaration"
                        );
                      var varDecl = (IVariableDeclaratorOperation) op.Parent!.Parent!;

                      var sb = ctx.EmptyStringBuilder();
                      sb.Append("{");
                      sb.Append(a.Pattern);
                      sb.Append("}");

                      replaceArguments(ctx, sb, iop);

                      sb.Replace("${varName}", varDecl.Symbol.ToString());
                      sb.Replace("${varType}", varDecl.Symbol.Type.ToDisplayString());

                      var parsedBlock = (BlockSyntax) SyntaxFactory.ParseStatement(sb.ToString());
                      ctx.ChangedStatements.Add(vdgop.Syntax, parsedBlock.Statements.ToArray<SyntaxNode>());
                    }
                    else {
                      throw new Exception(
                        $"Expected {nameof(IVariableDeclarationGroupOperation)}, got {parent4?.GetType()}");
                    }
                  });
              });
            }, diagnostic);

        if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, inlineType))
#pragma warning disable 618
          CodeGeneration.tryAttribute<Inline>(
#pragma warning restore 618
            attribute, _ => helper.builderInvocations.Add(method, (ctx, op) => {
              if (op is IInvocationOperation iop) {
                var parent = op.Parent;
                // if (parent is IExpressionStatementOperation statementOperation)
                {
                  var methodSyntax = (MethodDeclarationSyntax) method.DeclaringSyntaxReferences.Single().GetSyntax();
                  var body = methodSyntax.Body!;

                  var sourceSpan = iop.Syntax.GetLocation().SourceSpan;

                  var newName = $"INLINED_{sourceSpan.Start}_{sourceSpan.End}_{methodSyntax.Identifier}";

                  var parameters = methodSyntax.ParameterList;
                  if (parameters.Parameters.Count > 0)
                    parameters = methodSyntax.ParameterList.WithParameters(
                      methodSyntax.ParameterList.Parameters.Replace(
                        methodSyntax.ParameterList.Parameters[0],
                        methodSyntax.ParameterList.Parameters[0]
                          .WithModifiers(SF.TokenList())));

                  var localFunction = SF
                    .LocalFunctionStatement(methodSyntax.ReturnType, newName)
                    .WithParameterList(parameters)
                    .WithBody(body);

                  var ooo = ctx.Model.GetOperation(methodSyntax, CancellationToken.None);
                  var invocation = (InvocationExpressionSyntax) iop.Syntax;

                  // left.call(one, two);
                  var argumentsArray = iop.Arguments.Select(_ => {
                    var visited = ctx.Visit(_.Value.Syntax);
                    return visited is ExpressionSyntax es
                      // one, two
                      ? SF.Argument(es)
                      // left
                      : SF.Argument(SF.ParseExpression(visited.ToString()));
                  }).ToArray();

                  var arguments = SF.ArgumentList(SF.SeparatedList(argumentsArray));

                  var newInvocation = SF.InvocationExpression(SF.IdentifierName(newName), arguments);

                  // var updatedLine = SF.ExpressionStatement(newInvocation);
                  // new StatementSyntax[]{localFunction, updatedLine}

                  ctx.ChangedNodes.Add(iop.Syntax, newInvocation);
                  ctx.AddedStatements.Add(iop.Syntax, localFunction);
                }
              }
            }), diagnostic);
      }

      logTime?.Invoke("a3");

      // var namedTypeSymbols = CustomSymbolFinder.GetAllSymbols(compilation);

      implicits.AfterCheckAttributes(logTime);

      logTime?.Invoke("a4");

      var resolvedMacros = helper.builderInvocations.ToImmutable();
      var resolvedMacros2 = helper.builderDefinitions.ToImmutable();

      logTime?.Invoke("a5");

      var treeEdits = new ConcurrentBag<(SyntaxTree, CompilationUnitSyntax)>();
      helper.operations.AsParallel().ForAll(opFinder => {
        var root = opFinder.tree.GetCompilationUnitRoot();
        var model = opFinder.model;
        var ctx = new MacroCtx(model);

        foreach (var (mainOperation, tds) in opFinder.results) {
          // Console.WriteLine("Found Operation: " + operation);
          // Console.WriteLine(operation.Syntax);

          var descendants = mainOperation.DescendantsAndSelf().ToArray();
          // reverse the order for nested macros to work
          Array.Reverse(descendants);

          foreach (var descendantOperation in descendants) {
            switch (descendantOperation) {
              case IMethodReferenceOperation op: {
                if (resolvedMacros.ContainsKey(op.Method.OriginalDefinition))
                  diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
                    "ER0003", "Error", "Can't reference a macro.", "Error", DiagnosticSeverity.Error, true
                  ), op.Syntax.GetLocation()));
                break;
              }
              case IPropertyReferenceOperation op: {
                if (resolvedMacros.TryGetValue(op.Property.OriginalDefinition, out var act))
                  act(ctx, op);
                break;
              }
              case IInvocationOperation op: {
                var method = op.TargetMethod.OriginalDefinition;
                if (resolvedMacros.TryGetValue(method, out var act)) act(ctx, op);
                break;
              }
              case IObjectCreationOperation op when op.Constructor != null: {
                var method = op.Constructor.OriginalDefinition;
                if (resolvedMacros.TryGetValue(method, out var act)) act(ctx, op);
                break;
              }
            }
          }

          var symbol = model.GetDeclaredSymbol(mainOperation.Syntax);
          switch (symbol) {
            case IMethodSymbol ms: {
              if (resolvedMacros2.TryGetValue(ms, out var act)) act(ctx);
              break;
            }
            // this case is never reached, we use the code below for this
            // case IPropertySymbol ps: {
            //   if (tds != null) tryLazyProperty(ps, tds);
            //   break;
            // }
          }
        }

        var generatorCtx = new GeneratorCtx(root, model);
        foreach (var tds in generatorCtx.TypesInFile) {
          var symbol = model.GetDeclaredSymbol(tds);
          if (symbol == null) continue;
          foreach (var member in symbol.GetMembers()) {
            switch (member) {
              case IPropertySymbol propertySymbol:
                tryLazyProperty(propertySymbol, tds);
                break;
              // IMethodSymbol case gets called too many times (partial classes), we use the code above for this
              // case IMethodSymbol methodSymbol:
              //   if (resolvedMacros2.TryGetValue(methodSymbol, out var act)) act(ctx);
              //   break;
            }
          }
        }

        void tryLazyProperty(IPropertySymbol propertySymbol, TypeDeclarationSyntax tds) {
          var attributes = propertySymbol.GetAttributes();
          foreach (var attr in attributes) {
            if (!GeneratorCtx.TreeContains(attr.ApplicationSyntaxReference, tds)) continue;
            if (attr.AttributeClass == null) continue;
            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, lazyPropertyType))
              CodeGeneration.tryAttribute<LazyProperty>(attr, _ => {
                if (propertySymbol.SetMethod != null)
                  throw new Exception("Lazy Property should not have a setter");
                if (propertySymbol.GetMethod == null) throw new Exception("Lazy Property should have a getter");
                // sometimes we want an Implicit attribute on this property
                // if (attributes.Length > 1)
                // throw new Exception("Lazy Property should not have other attributes");
                var originalSyntax = propertySymbol.DeclaringSyntaxReferences.Single().GetSyntax();
                var syntax = (PropertyDeclarationSyntax) ctx.Visit(originalSyntax);

                var baseName = syntax.Identifier;
                var backingValueName = SF.Identifier("__lazy_value_" + baseName.Text);
                var backingInitName = SF.Identifier("__lazy_init_" + baseName.Text);

                var modifiers = propertySymbol.IsStatic
                  ? SF.TokenList(SF.Token(SyntaxKind.StaticKeyword))
                  : SF.TokenList();

                // object __lazy_value_baseName;
                // int? __lazy_value_baseName;
                var variableDecl = SF.FieldDeclaration(SF.VariableDeclaration(
                  propertySymbol.Type.IsValueType ? SF.NullableType(syntax.Type) : syntax.Type,
                  SF.SingletonSeparatedList(SF.VariableDeclarator(backingValueName))
                )).WithModifiers(modifiers);

                // object baseName => __lazy_value_baseName ??= __lazy_init_baseName;
                // object baseName => __lazy_value_baseName ??= {expression};
                var originalReplacement = SF.PropertyDeclaration(
                    syntax.Type,
                    baseName
                  ).WithExpressionBody(SF.ArrowExpressionClause(SF.AssignmentExpression(
                    SyntaxKind.CoalesceAssignmentExpression,
                    SF.IdentifierName(backingValueName),
                    syntax.ExpressionBody?.Expression ?? SF.IdentifierName(backingInitName)
                  )))
                  .WithSemicolonToken(SF.Token(SyntaxKind.SemicolonToken))
                  .WithModifiers(syntax.Modifiers);

                ctx.ChangedStatements.Add(originalSyntax, new SyntaxNode?[] {
                  variableDecl,
                  syntax.ExpressionBody == null
                    ? syntax.WithIdentifier(backingInitName)
                      .WithModifiers(modifiers)
                      .WithAttributeLists(SF.List<AttributeListSyntax>())
                    : null,
                  originalReplacement
                });
              }, diagnostic);
          }
        }

        var newRoot = root;

        if (ctx.ChangedNodes.Any() || ctx.ChangedStatements.Any()) {
          var updatedTree = (CompilationUnitSyntax) ctx.Visit(root);
          ctx.CompleteVisit(diagnostic);
          // var updatedTree = root.ReplaceNodes(changes.Keys, (a, b) => changes[a]);
          // Console.WriteLine(updatedTree.GetText());
          newRoot = updatedTree;
        }

        if (newRoot != root)
          // TODO: do not normalize whitespace for the whole file
          // need to fix whitespace in MacroReplacer first
          treeEdits.Add((opFinder.tree, newRoot.NormalizeWhitespace()));
      });
      logTime?.Invoke("a6");
      compilation = EditTrees(compilation, sourceMap, treeEdits.ToArray(), settings, generatedFiles);
      logTime?.Invoke("a7");
      return compilation;
    }

    public static CSharpCompilation EditTrees(
      CSharpCompilation compilation,
      Dictionary<string, SyntaxTree> sourceMap,
      IEnumerable<(SyntaxTree, CompilationUnitSyntax)> treeEdits,
      GenerationSettings settings,
      List<CodeGeneration.GeneratedCsFile> generatedFiles
    ) {
      foreach (var (tree, syntax) in treeEdits) {
        var originalFilePath = settings.GetRelativePath(tree.FilePath);
        var relativePath = originalFilePath.EnsureDoesNotEndWith(".cs") + ".transformed.cs";
        var editedFilePath = Path.Combine(settings.macrosFolder, relativePath);

        var newTree = tree.WithRootAndOptions(syntax, tree.Options).WithFilePath(editedFilePath);
        sourceMap[tree.FilePath] = newTree;
        compilation = compilation.ReplaceSyntaxTree(tree, newTree);

        generatedFiles.Add(new CodeGeneration.GeneratedCsFile(
          originalFilePath, relativePath, tree.GetRoot().GetLocation(), newTree, true
        ));
        // Directory.CreateDirectory(Path.GetDirectoryName(editedFilePath));
        // File.WriteAllText(editedFilePath, newTree.GetText().ToString());
      }

      return compilation;
    }

    public class MacroCtx {
      public readonly Dictionary<SyntaxNode, SyntaxNode> AddedStatements = new();

      public readonly Dictionary<SyntaxNode, SyntaxNode> ChangedNodes = new();

      public readonly Dictionary<SyntaxNode, SyntaxNode?[]> ChangedStatements = new();

      public readonly SemanticModel Model;
      readonly MacroReplacer Replacer;
      readonly StringBuilder stringBuilder = new();

      public MacroCtx(SemanticModel model) {
        Model = model;
        Replacer = new MacroReplacer(this);
      }

      public StringBuilder EmptyStringBuilder() {
        stringBuilder.Clear();
        return stringBuilder;
      }

      public SyntaxNode Visit(SyntaxNode node) {
        Replacer.Reset();
        return Replacer.Visit(node);
      }

      public void CompleteVisit(List<Diagnostic> diagnostic) {
        var unchanged = ChangedNodes.Keys.Concat(ChangedStatements.Keys).Except(Replacer.successfulEdits);
        foreach (var node in unchanged)
          diagnostic.Add(Diagnostic.Create(
            new DiagnosticDescriptor(
              "ER0003",
              "Error",
              "Macro was not replaced. This is a bug in the compiler.",
              "Error",
              DiagnosticSeverity.Error,
              true
            ),
            node.GetLocation()
          ));
      }
    }
  }

  public class MacroProcessorError : Exception {
    public MacroProcessorError(string message) : base(message) { }
  }

  public class RootOperationsFinder : CSharpSyntaxWalker {
    public readonly SemanticModel model;
    public readonly SyntaxTree tree;
    public readonly List<(IOperation op, TypeDeclarationSyntax? tds)> results = new();

    // public TimeSpan tsNull, tsOther;

    readonly Stack<TypeDeclarationSyntax> typeStack = new();

    public RootOperationsFinder(SemanticModel model, SyntaxTree tree) {
      this.model = model;
      this.tree = tree;
    }

    public override void Visit(SyntaxNode? node) {
      var addedToStack = false;
      if (node is TypeDeclarationSyntax tds) {
        typeStack.Push(tds);
        addedToStack = true;
      }

      try {
        if (node == null) return;

        // var sw = Stopwatch.StartNew();

        var operation = model.GetOperation(node);

        // if (operation == null) {
        //   tsNull += sw.Elapsed;
        // }
        // else {
        //   var elapsed = sw.Elapsed;
        //   if (elapsed.TotalMilliseconds > 200) Console.WriteLine(elapsed + "   " + node.ToFullString().Substring(0, 200));
        //   tsOther += elapsed;
        // }

        if (operation == null) base.Visit(node);
        else results.Add((operation, typeStack.Count > 0 ? typeStack.Peek() : null));
      }
      finally {
        if (addedToStack) typeStack.Pop();
      }
    }
  }

  public class Walker : OperationWalker {
    int ident;

    public override void Visit(IOperation? operation) {
      for (var i = 0; i < ident; i++) Console.Write("  ");
      Console.WriteLine(operation?.Kind.ToString());
      ident++;
      base.Visit(operation);
      ident--;
    }
  }

  public class CustomSymbolFinder {
    public static List<INamedTypeSymbol> GetAllSymbols(Compilation compilation) {
      var visitor = new FindAllSymbolsVisitor();
      visitor.Visit(compilation.GlobalNamespace);
      return visitor.AllTypeSymbols;
    }

    class FindAllSymbolsVisitor : SymbolVisitor {
      public List<INamedTypeSymbol> AllTypeSymbols { get; } = new();

      public override void VisitNamespace(INamespaceSymbol symbol) {
        Parallel.ForEach(symbol.GetMembers(), s => s.Accept(this));
      }

      public override void VisitNamedType(INamedTypeSymbol symbol) {
        AllTypeSymbols.Add(symbol);
        foreach (var childSymbol in symbol.GetTypeMembers()) base.Visit(childSymbol);
      }
    }
  }
}
