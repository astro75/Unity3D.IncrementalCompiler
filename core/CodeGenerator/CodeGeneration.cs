using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using GenerationAttributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static System.String;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IncrementalCompiler {
  public class GenerationSettings {
    readonly Uri baseDirectoryUri;
    public readonly string macrosFolder;
    public readonly string partialsFolder;
    public readonly string? txtForPartials;

    public GenerationSettings(string partialsFolder, string macrosFolder, string? txtForPartials,
      string baseDirectory) {
      this.partialsFolder = partialsFolder;
      this.macrosFolder = macrosFolder;
      this.txtForPartials = txtForPartials;

      if (!(
        baseDirectory.EndsWith("\\", StringComparison.Ordinal)
        || baseDirectory.EndsWith("/", StringComparison.Ordinal)
      )) baseDirectory += "\\";
      baseDirectoryUri = new Uri(Path.GetFullPath(baseDirectory));
    }

    public string GetRelativePath(string path) {
      return baseDirectoryUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString();
    }
  }

  internal class GeneratorCtx {
    public readonly SemanticModel Model;
    public readonly List<MemberDeclarationSyntax> NewMembers = new();
    public readonly ImmutableArray<TypeDeclarationSyntax> TypesInFile;
    public readonly List<INamedTypeSymbol> TypesWithMacros = new();

    public GeneratorCtx(CompilationUnitSyntax root, SemanticModel model) {
      Model = model;
      TypesInFile = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
    }

    public void AddMacroMethod(INamedTypeSymbol symbol) {
      TypesWithMacros.Add(symbol);
    }

    public static bool TreeContains(SyntaxReference? syntaxRef, TypeDeclarationSyntax tds) {
      return
        // filter out partial classes in other files, or even the same file
        syntaxRef != null
        // compare local position in file
        && tds.Span.Contains(syntaxRef.Span)
        // compare file
        && syntaxRef.SyntaxTree == tds.SyntaxTree;
    }
  }

  public static partial class CodeGeneration {
    static readonly Type caseType = typeof(RecordAttribute);

    static readonly HashSet<SyntaxKind> kindsForExtensionClass = new(new[] {
      SyntaxKind.PublicKeyword, SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword
    });

    // CSharpErrorMessageFormat is default for ToDisplayString
    static readonly SymbolDisplayFormat format =
      SymbolDisplayFormat
        .CSharpErrorMessageFormat
        .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included);

    static string Block(IEnumerable<string> contents) {
      return "{" + Join("\n", contents) + "}";
    }

    static string Block(params string[] contents) {
      return Block((IEnumerable<string>) contents);
    }

    // Directory.Delete(recursive: true) throws an exception in some cases
    static void DeleteFilesAndFoldersRecursively(string targetDir) {
      foreach (var file in Directory.GetFiles(targetDir))
        File.Delete(file);

      foreach (var subDir in Directory.GetDirectories(targetDir))
        DeleteFilesAndFoldersRecursively(subDir);

      try {
        Directory.Delete(targetDir);
      }
      catch (IOException) {
        // it fails one time if Windows file explorer is opened in targetDir
        try {
          Directory.Delete(targetDir);
        }
        catch (DirectoryNotFoundException) {
          // we get this error on mac sometimes
        }
      }
    }

    public static void DeleteFilesRecursively(string targetDir) {
      foreach (var file in Directory.GetFiles(targetDir)) File.Delete(file);
      foreach (var subDir in Directory.GetDirectories(targetDir)) DeleteFilesRecursively(subDir);
    }

    public static void tryAttribute<A>(AttributeData attr, Action<A> a, List<Diagnostic> diagnostic)
      where A : Attribute {
      try {
        var instance = CreateAttributeByReflection<A>(attr);
        a(instance);
      }
      catch (Exception e) {
        diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
          "ER0001",
          "Error",
          $"Compiler error for attribute {typeof(A).Name}: {e.Message}({e.Source}) at {e.StackTrace}",
          "Error",
          DiagnosticSeverity.Error,
          true
        ), AttrLocation(attr)));
      }
    }

    public static (CSharpCompilation, List<Diagnostic>) Run(
      bool incrementalRun,
      CSharpCompilation compilation,
      ImmutableArray<SyntaxTree> trees,
      CSharpParseOptions parseOptions,
      string assemblyName,
      GeneratedFilesMapping filesMapping,
      Dictionary<string, SyntaxTree> sourceMap,
      GenerationSettings settings,
      List<GeneratedCsFile> generatedCsFiles
    ) {
      var oldCompilation = compilation;
      var diagnostic = new List<Diagnostic>();

      void tryAttributeLocal<A>(AttributeData attr, Action<A> a) where A : Attribute {
        tryAttribute(attr, a, diagnostic);
      }

      var typeAttributes = new Dictionary<
        INamedTypeSymbol,
        Action<AttributeData, GeneratorCtx, TypeDeclarationSyntax, INamedTypeSymbol>
      >();

      var methodAttributes = new Dictionary<
        INamedTypeSymbol,
        Action<AttributeData, GeneratorCtx, TypeDeclarationSyntax, IMethodSymbol>
      >();

      var parameterAttributes = new Dictionary<
        INamedTypeSymbol,
        Action<AttributeData, GeneratorCtx, TypeDeclarationSyntax, IParameterSymbol>
      >();

      {
        addAttribute<RecordAttribute>((instance, ctx, tds, symbol) => {
          ctx.NewMembers.AddRange(
            GenerateCaseClass(instance, ctx.Model, tds, symbol)
              .Select(generatedClass => AddAncestors(tds, generatedClass, false))
          );
        });

        addAttribute<SingletonAttribute>((instance, ctx, tds, symbol) => {
          if (tds is ClassDeclarationSyntax cds)
            ctx.NewMembers.Add(
              AddAncestors(tds, GenerateSingleton(cds), false)
            );
          else
            throw new Exception("Can only be used on a class");
        });

        addAttribute<MatcherAttribute>((instance, ctx, tds, symbol) => {
          var matcher = GenerateMatcher(ctx.Model, tds, instance, ctx.TypesInFile, symbol);
          ctx.NewMembers.Add(AddAncestors(tds, matcher, true));
        });

        addMacroAttributeMethod<SimpleMethodMacro>();
        addMacroAttributeMethod<StatementMethodMacro>();
        addMacroAttributeMethod<VarMethodMacro>();
#pragma warning disable 618
        addMacroAttributeMethod<Inline>();
#pragma warning restore 618
        addMacroAttributeMethod<ImplicitPassThrough>();

        addMacroAttributeParameter<Implicit>();

        void addAttribute<A>(Action<A, GeneratorCtx, TypeDeclarationSyntax, INamedTypeSymbol> act) where A : Attribute {
          var compilationType = compilation.GetTypeByMetadataName(typeof(A).FullName);
          if (compilationType != null)
            typeAttributes.Add(
              compilationType,
              (attr, ctx, tds, symbol) => tryAttributeLocal<A>(attr, instance => act(instance, ctx, tds, symbol))
            );
        }

        void addMacroAttributeMethod<A>() {
          var compilationType = compilation.GetTypeByMetadataName(typeof(A).FullName);
          if (compilationType != null)
            methodAttributes.Add(
              compilationType,
              (attr, ctx, tds, symbol) => ctx.AddMacroMethod(symbol.ContainingType)
            );
        }

        void addMacroAttributeParameter<A>() {
          var compilationType = compilation.GetTypeByMetadataName(typeof(A).FullName);
          if (compilationType != null)
            parameterAttributes.Add(
              compilationType,
              (attr, ctx, tds, symbol) => ctx.AddMacroMethod(symbol.ContainingType)
            );
        }
      }

      var results = new ConcurrentBag<IGenerationResult>();
      var typesWithMacrosResults = new ConcurrentBag<INamedTypeSymbol>();

      trees.AsParallel().ForAll(originalTree => {
        var tree = originalTree;

        var model = oldCompilation.GetSemanticModel(tree);
        var root = tree.GetCompilationUnitRoot();

        var treeEdited = false;
        var editsList = new List<(SyntaxNode, SyntaxNode)>();
        // void replaceSyntax(SyntaxNode oldNode, SyntaxNode newNode) {
        //     treeEdited = true;
        //     editsList.Add((oldNode, newNode));
        // }

        var ctx = new GeneratorCtx(root, model);

        foreach (var tds in ctx.TypesInFile) {
          var symbol = model.GetDeclaredSymbol(tds);
          if (symbol == null) continue;
          JavaClassFile? javaClassFile = null;
          foreach (var attr in symbol.GetAttributes()) {
            if (!GeneratorCtx.TreeContains(attr.ApplicationSyntaxReference, tds)) continue;
            if (attr.AttributeClass == null) continue;

            if (typeAttributes.TryGetValue(attr.AttributeClass, out var action)) action(attr, ctx, tds, symbol);

            // java attributes disabled for now
            // var attrClassName = attr.AttributeClass.ToDisplayString();
            // if (attrClassName == typeof(JavaClassAttribute).FullName)
            // {
            //     tryAttributeLocal<JavaClassAttribute>(attr, instance =>
            //     {
            //         javaClassFile = new JavaClassFile(symbol, module: instance.Module, imports: instance.Imports, classBody: instance.ClassBody, attrLocation(attr));
            //         newMembers = newMembers.Add(AddAncestors(
            //             tds,
            //             CreatePartial(tds, javaClassFile.GenerateMembers(), Extensions.EmptyBaseList),
            //             onlyNamespace: false
            //          ));
            //     });
            // }
            // if (attrClassName == typeof(JavaListenerInterfaceAttribute).FullName)
            // {
            //     tryAttributeLocal<JavaListenerInterfaceAttribute>(attr, instance =>
            //     {
            //         var javaInterface = new JavaClassFile(symbol, module: instance.Module, imports: "", classBody: "", attrLocation(attr));
            //         result = result.Add(new GeneratedJavaFile(
            //             sourcePath: tree.FilePath,
            //             location: attrLocation(attr),
            //             javaFile: new JavaFile(
            //                 module: javaInterface.Module,
            //                 path: javaInterface.JavaFilePath(),
            //                 contents: javaInterface.GenerateJavaInterface()
            //             )
            //         ));
            //         newMembers = newMembers.Add(AddAncestors(tds, javaInterface.GetInterfaceClass(), onlyNamespace: false));
            //     });
            // }
          }

          var newClassMembers = ImmutableArray<string>.Empty;
          foreach (var member in symbol.GetMembers())
            switch (member) {
              case IFieldSymbol fieldSymbol:
                foreach (var attr in fieldSymbol.GetAttributes()) {
                  if (!GeneratorCtx.TreeContains(attr.ApplicationSyntaxReference, tds)) continue;
                  if (attr.AttributeClass == null) continue;
                  var attrClassName = attr.AttributeClass.ToDisplayString();
                  if (attrClassName == typeof(PublicAccessor).FullName)
                    tryAttributeLocal<PublicAccessor>(attr,
                      _ => { newClassMembers = newClassMembers.Add(GenerateAccessor(fieldSymbol, model)); });
                }

                break;
              case IMethodSymbol methodSymbol:
                foreach (var attr in methodSymbol.GetAttributes()) {
                  if (!GeneratorCtx.TreeContains(attr.ApplicationSyntaxReference, tds)) continue;
                  if (attr.AttributeClass == null) continue;
                  if (methodAttributes.TryGetValue(attr.AttributeClass, out var action))
                    action(attr, ctx, tds, methodSymbol);
                  // var attrClassName = attr.AttributeClass.ToDisplayString();
                  // if (attrClassName == typeof(JavaMethodAttribute).FullName)
                  // {
                  //     tryAttributeLocal<JavaMethodAttribute>(attr, instance =>
                  //     {
                  //         if (javaClassFile == null) throw new Exception(
                  //             $"must be used together with {nameof(JavaClassAttribute)}"
                  //         );
                  //         javaClassFile.AddMethod(instance.MethodBody, methodSymbol);
                  //         var syntaxes = methodSymbol.DeclaringSyntaxReferences;
                  //         if (syntaxes.Length != 1) throw new Exception($"code must be in one place");
                  //         var syntax = (BaseMethodDeclarationSyntax) syntaxes[0].GetSyntax();
                  //         var replacedSyntax = javaClassFile.GenerateMethod(methodSymbol, syntax);
                  //         replaceSyntax(syntax, replacedSyntax);
                  //     });
                  // }
                }

                foreach (var parameterSymbol in methodSymbol.Parameters)
                foreach (var attr in parameterSymbol.GetAttributes()) {
                  if (!GeneratorCtx.TreeContains(attr.ApplicationSyntaxReference, tds)) continue;
                  if (attr.AttributeClass == null) continue;
                  if (parameterAttributes.TryGetValue(attr.AttributeClass, out var action))
                    action(attr, ctx, tds, parameterSymbol);
                }

                break;
            }

          if (newClassMembers.Length > 0)
            ctx.NewMembers.Add(AddAncestors(
              tds,
              CreatePartial(tds, ParseClassMembers(Join("\n", newClassMembers)), Extensions.EmptyBaseList),
              false
            ));

          if (javaClassFile != null)
            results.Add(new GeneratedJavaFile(
              tree.FilePath,
              javaClassFile.Location,
              new JavaFile(
                javaClassFile.Module,
                javaClassFile.JavaFilePath(),
                javaClassFile.GenerateJava()
              )
            ));
        }

        if (treeEdited) {
          var newRoot = root.ReplaceNodes(
            editsList.Select(t => t.Item1),
            (toReplace, _) => editsList.First(t => t.Item1 == toReplace).Item2
          );
          results.Add(new ModifiedFile(tree, newRoot));
        }

        if (ctx.NewMembers.Count > 0) {
          var treePath = settings.GetRelativePath(tree.FilePath);
          var relativePath = treePath.EnsureDoesNotEndWith(".cs") + ".partials.cs";
          var nt = CSharpSyntaxTree.Create(
            SF.CompilationUnit()
              .WithUsings(CleanUsings(root.Usings))
              .WithLeadingTrivia(SyntaxTriviaList.Create(SyntaxFactory.Comment("// ReSharper disable all")))
              .WithMembers(SF.List(ctx.NewMembers))
              .NormalizeWhitespace(),
            path: Path.Combine(settings.partialsFolder, relativePath),
            options: parseOptions,
            encoding: Encoding.UTF8);
          results.Add(new GeneratedCsFile(
            SourcePath: treePath, RelativePath: relativePath, Tree: nt,
            Location: root.GetLocation(), TransformedFile: false
          ));
        }

        foreach (var t in ctx.TypesWithMacros) {
          typesWithMacrosResults.Add(t);
        }
      });

      {
        var typesWithMacros = typesWithMacrosResults
          .Distinct()
          .OrderBy(_ => _.Name)
          .ToArray();

        if (typesWithMacros.Length > 0) {
          var typesString = Join(", ", typesWithMacros.Select(_ => {
            return $"typeof({nestedTypeName(_)})";
            static string nestedTypeName(INamedTypeSymbol symbol) {
              var genericAddition = symbol.IsGenericType ? $"<{new string(',', symbol.Arity - 1)}>" : "";
              var baseName = symbol.ContainingSymbol switch {
                INamedTypeSymbol nts => nestedTypeName(nts) + ".",
                INamespaceSymbol ns when ns.IsGlobalNamespace => "global::",
                { } s => s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".",
                _ => ""
              };
              return $"{baseName}{symbol.Name}{genericAddition}";
            }
          }));

          var syntax = CSharpSyntaxTree.ParseText(
            "// generated\n" +
            $"[assembly: {typeof(TypesWithMacroAttributes).FullName}(new global::System.Type[]{{{typesString}}})]"
          ).GetCompilationUnitRoot();

          var name = "MacroList.cs";
          var nt = CSharpSyntaxTree.Create(
            syntax,
            path: Path.Combine(settings.partialsFolder, name),
            options: parseOptions,
            encoding: Encoding.UTF8);

          results.Add(new GeneratedCsFile(
            name, name, Tree: nt, Location: Location.None, TransformedFile: false
          ));
        }
      }

      var csFiles = results.OfType<GeneratedCsFile>().ToArray();
      compilation = compilation.AddSyntaxTrees(csFiles.Select(_ => _.Tree));

      generatedCsFiles.AddRange(csFiles);

      foreach (var file in csFiles) {
        sourceMap[file.FullPath] = file.Tree;
        filesMapping.Add(file.SourcePath, file.FullPath);

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
      }

      foreach (var file in results.OfType<GeneratedJavaFile>())
        if (!filesMapping.tryAddJavaFile(file.SourcePath, file.JavaFile))
          diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
            "ER0002", "Error", $"Could not generate java file '{file.JavaFile.Path}'. File already exists.", "Error",
            DiagnosticSeverity.Error, true
          ), file.Location));

      compilation = MacroProcessor.EditTrees(
        compilation, sourceMap, results.OfType<ModifiedFile>().Select(f => (f.From, f.To)), settings, generatedCsFiles
      );
      compilation = filesMapping.updateCompilation(compilation, parseOptions, assemblyName, settings.partialsFolder);
      if (settings.txtForPartials != null)
        File.WriteAllLines(
          settings.txtForPartials,
          filesMapping.filesDict.Values
            .SelectMany(_ => _)
            .Select(path => path.Replace("/", "\\")));
      return (compilation, diagnostic);
    }

    static IEnumerable<A> NullableAsEnumerable<A>(A? maybeValue) where A : class {
      return maybeValue != null ? new[] {maybeValue} : Enumerable.Empty<A>();
    }

    static Location AttrLocation(AttributeData attr) {
      return attr.ApplicationSyntaxReference!.GetSyntax().GetLocation();
    }

    static void SetNamedArguments(Type type, AttributeData attributeData, Attribute instance) {
      foreach (var arg in attributeData.NamedArguments) {
        // if some arguments are invalid they do not appear in NamedArguments list
        // because of that we do not check for errors
        var prop = type.GetProperty(arg.Key);
        prop?.SetValue(instance, arg.Value.Value);
      }
    }

    static A CreateAttributeByReflection<A>(AttributeData attributeData) where A : Attribute {
      var type = typeof(A);
      var arguments = attributeData.ConstructorArguments;
      var ctor = type.GetConstructors().First(ci => ci.GetParameters().Length == arguments.Length);
      var res = (A) ctor.Invoke(arguments.Select(a => a.Value).ToArray());
      SetNamedArguments(type, attributeData, res);
      return res;
    }

    static string GenerateAccessor(IFieldSymbol fieldSymbol, SemanticModel model) {
      var name = fieldSymbol.Name;
      var newName = name.TrimStart('_');
      var position = fieldSymbol.DeclaringSyntaxReferences[0].Span.Start;
      var type = fieldSymbol.Type.ToMinimalDisplayString(model, position, format);

      if (name == newName) newName += "_";
      return $"public {type} {newName} => {name};";
    }

    static MemberDeclarationSyntax GenerateSingleton(
      ClassDeclarationSyntax tds
    ) {
      var members = ParseClassMembers(
        $"private {tds.Identifier}(){{}}" +
        $"public static readonly {tds.Identifier} instance = new {tds.Identifier}();");
      var partialClass = CreatePartial(tds, members, Extensions.EmptyBaseList);
      return partialClass;
    }

    static MemberDeclarationSyntax GenerateMatcher(
      SemanticModel model, TypeDeclarationSyntax tds,
      MatcherAttribute attribute, ImmutableArray<TypeDeclarationSyntax> typesInFile,
      INamedTypeSymbol baseTypeSymbol
    ) {
      // TODO: ban extending this class in different files
      // TODO: generics ?

      var symbols = typesInFile
        .Select(t => model.GetDeclaredSymbol(t))
        .OfType<INamedTypeSymbol>()
        // move current symbol to back
        .OrderBy(s => SymbolEqualityComparer.Default.Equals(s, baseTypeSymbol));

      IEnumerable<INamedTypeSymbol> findTypes() {
        switch (tds) {
          case ClassDeclarationSyntax _:
            return symbols.Where(s => {
              if (!baseTypeSymbol.IsAbstract && SymbolEqualityComparer.Default.Equals(s, baseTypeSymbol)) return true;
              return SymbolEqualityComparer.Default.Equals(s.BaseType, baseTypeSymbol);
            });
          case InterfaceDeclarationSyntax _:
            return symbols.Where(s => s.Interfaces.Contains(baseTypeSymbol));
          default:
            throw new Exception($"{tds} - matcher should be added on class or interface");
        }
      }

      var childTypes = findTypes();

      /*
      public void match(Action<One> t1, Action<Two> t2) {
          var val1 = this as One;
          if (val1 != null) {
              t1(val1);
              return;
          }
          var val2 = this as Two;
          if (val2 != null) {
              t2(val2);
              return;
          }
      }
      */

      string toLowerFirstLetter(string s) {
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
      }

      var childNames = childTypes.Select(s => (fullName: s.ToString(), varName: toLowerFirstLetter(s.Name))).ToArray();

      var firstParam = new[] {$"this {baseTypeSymbol} obj"};

      string VoidMatch() {
        var parameters = Join(", ",
          firstParam.Concat(childNames.Select(t => $"System.Action<{t.fullName}> {t.varName}")));
        var body = Join("\n", childNames.Select(t =>
          $"var val_{t.varName} = obj as {t.fullName};" +
          $"if (val_{t.varName} != null) {{ {t.varName}(val_{t.varName}); return; }}"
        )) + $"throw new NullReferenceException(\"Expected to have type of {baseTypeSymbol}, but received null instead\");";

        return $"public static void voidMatch({parameters}) {{{body}}}";
      }

      string Match() {
        var parameters = Join(", ",
          firstParam.Concat(childNames.Select(t => $"System.Func<{t.fullName}, A> {t.varName}")));
        var body = Join("\n", childNames.Select(t =>
          $"var val_{t.varName} = obj as {t.fullName};" +
          $"if (val_{t.varName} != null) return {t.varName}(val_{t.varName});"));

        return $"public static A match<A>({parameters}) {{" +
               $"{body}" +
               "throw new System.ArgumentOutOfRangeException(\"obj\", obj, \"Should never reach this\");" +
               "}";
      }

      var className = IsNullOrWhiteSpace(attribute.ClassName)
        ? tds.Identifier + "Matcher"
        : attribute.ClassName;
      return CreateStatic(className, tds, ParseClassMembers(VoidMatch() + Match()));
    }

    static string JoinCommaSeparated<A>(this IEnumerable<A> collection, Func<A, string> mapper) {
      return collection
        .Select(mapper)
        .Tap(_ => Join(", ", _));
    }

    static SyntaxList<MemberDeclarationSyntax> GenerateStaticApply(
      TypeDeclarationSyntax cds, ICollection<FieldOrProp> props
    ) {
      var genericArgsStr = cds.TypeParameterList?.ToFullString().TrimEnd() ?? "";
      var funcParamsStr = JoinCommaSeparated(props, p => p.type + " " + p.identifier);
      var funcArgs = JoinCommaSeparated(props, p => p.identifier);

      return ParseClassMembers(
        $"public static {cds.Identifier.ValueText}{genericArgsStr} a{genericArgsStr}" +
        $"({funcParamsStr}) => new {cds.Identifier.ValueText}{genericArgsStr}({funcArgs});"
      );
    }

    static TypeDeclarationSyntax CreatePartial(
      TypeDeclarationSyntax originalType, IEnumerable<MemberDeclarationSyntax> newMembers, BaseListSyntax? baseList
    ) {
      return CreateType(
        originalType.Kind(),
        originalType.Identifier,
        originalType.Modifiers.Add(SyntaxKind.PartialKeyword),
        originalType.TypeParameterList,
        SF.List(newMembers),
        baseList
      );
    }

    static TypeDeclarationSyntax CreateStatic(
      string className,
      TypeDeclarationSyntax originalType,
      IEnumerable<MemberDeclarationSyntax> newMembers
    ) {
      return SF.ClassDeclaration(className)
        .WithModifiers(SF
          .TokenList(originalType.Modifiers.Where(k => kindsForExtensionClass.Contains(k.Kind())))
          .Add(SyntaxKind.StaticKeyword))
        .WithMembers(SF.List(newMembers));
    }

    public static TypeDeclarationSyntax CreateType(
      SyntaxKind kind, SyntaxToken identifier, SyntaxTokenList modifiers, TypeParameterListSyntax? typeParams,
      SyntaxList<MemberDeclarationSyntax> members, BaseListSyntax? baseList
    ) {
      switch (kind) {
        case SyntaxKind.ClassDeclaration:
          return SF.ClassDeclaration(identifier)
            .WithModifiers(modifiers)
            .WithTypeParameterList(typeParams)
            .WithMembers(members)
            .WithBaseList(baseList);
        case SyntaxKind.StructDeclaration:
          return SF.StructDeclaration(identifier)
            .WithModifiers(modifiers)
            .WithTypeParameterList(typeParams)
            .WithMembers(members)
            .WithBaseList(baseList);
        case SyntaxKind.InterfaceDeclaration:
          return SF.InterfaceDeclaration(identifier)
            .WithModifiers(modifiers)
            .WithTypeParameterList(typeParams)
            .WithMembers(members)
            .WithBaseList(baseList);
        default:
          throw new ArgumentOutOfRangeException(kind.ToString());
      }
    }

    /// <summary>
    ///   Copies namespace and class hierarchy from the original <see cref="memberNode" />
    /// </summary>
    // stolen from CodeGeneration.Roslyn
    static MemberDeclarationSyntax AddAncestors(
      MemberDeclarationSyntax memberNode, MemberDeclarationSyntax generatedType, bool onlyNamespace
    ) {
      // Figure out ancestry for the generated type, including nesting types and namespaces.
      foreach (var ancestor in memberNode.Ancestors())
        switch (ancestor) {
          case NamespaceDeclarationSyntax a:
            generatedType =
              SF.NamespaceDeclaration(a.Name)
                .WithUsings(CleanUsings(a.Usings))
                .WithMembers(SF.SingletonList(generatedType));
            break;
          case ClassDeclarationSyntax a:
            if (onlyNamespace) break;
            generatedType = a
              .WithMembers(SF.SingletonList(generatedType))
              .WithModifiers(a.Modifiers.Add(SyntaxKind.PartialKeyword))
              .WithoutTrivia()
              .WithCloseBraceToken(a.CloseBraceToken.WithoutTrivia())
              .WithBaseList(Extensions.EmptyBaseList)
              .WithAttributeLists(Extensions.EmptyAttributeList);
            break;
          case StructDeclarationSyntax a:
            if (onlyNamespace) break;
            generatedType = a
              .WithMembers(SF.SingletonList(generatedType))
              .WithModifiers(a.Modifiers.Add(SyntaxKind.PartialKeyword))
              .WithoutTrivia()
              .WithCloseBraceToken(a.CloseBraceToken.WithoutTrivia())
              .WithBaseList(Extensions.EmptyBaseList)
              .WithAttributeLists(Extensions.EmptyAttributeList);
            break;
        }

      return generatedType;
    }

    static SyntaxList<UsingDirectiveSyntax> CleanUsings(SyntaxList<UsingDirectiveSyntax> usings) {
      return SF.List(usings.Select(u =>
        u.WithUsingKeyword(u.UsingKeyword.WithoutTrivia())
      ));
    }

    static ClassDeclarationSyntax ParseClass(string syntax) {
      var cls = (ClassDeclarationSyntax) CSharpSyntaxTree.ParseText(syntax).GetCompilationUnitRoot().Members[0];
      return cls;
    }

    static SyntaxList<MemberDeclarationSyntax> ParseClassMembers(string syntax) {
      var cls = (ClassDeclarationSyntax) CSharpSyntaxTree.ParseText($"class C {{ {syntax} }}").GetCompilationUnitRoot()
        .Members[0];
      return cls.Members;
    }

    static BlockSyntax ParseBlock(string syntax) {
      return (BlockSyntax) SF.ParseStatement("{" + syntax + "}");
    }

    public static string Quote(string s) {
      return $"\"{s}\"";
    }

    public partial class GeneratedFilesMapping {
      public readonly Dictionary<string, List<string>> filesDict = new Dictionary<string, List<string>>();

      static void AddValue<A>(Dictionary<string, List<A>> dict, string key, A value) {
        if (!dict.ContainsKey(key)) dict[key] = new List<A>();
        dict[key].Add(value);
      }

      static IEnumerable<A> Enumerate<A>(Dictionary<string, List<A>> dict) {
        return dict.Values.SelectMany(_ => _);
      }

      public void Add(string key, string value) {
        AddValue(filesDict, key, value);
      }

      static string AsVerbatimString(string str) {
        return $"@\"{str.Replace("\"", "\"\"")}\"";
      }


      public void RemoveFiles(IEnumerable<string> filesToRemove) {
        foreach (var filePath in filesToRemove) {
          if (filesDict.TryGetValue(filePath, out var generatedFiles)) {
            foreach (var generatedFile in generatedFiles)
              if (File.Exists(generatedFile))
                File.Delete(generatedFile);
            filesDict.Remove(filePath);
          }

          maybeRemoveJavaFile(filePath);
        }
      }
    }

    interface IGenerationResult { }

    record ModifiedFile(SyntaxTree From, CompilationUnitSyntax To) : IGenerationResult;

    public record GeneratedFile(string SourcePath, Location Location) : IGenerationResult;

    // string sourcePath, string relativePath, Location location, SyntaxTree tree, bool transformedFile
    public record GeneratedCsFile(
      string SourcePath, string RelativePath, Location Location, SyntaxTree Tree, bool TransformedFile
    ) : GeneratedFile(SourcePath, Location) {
      public string FullPath => Tree.FilePath;
      public string Contents => Tree.GetText().ToString();
    }

    public record CaseClass(
      TypeDeclarationSyntax caseClass, TypeDeclarationSyntax? companion
    ) : IEnumerable<TypeDeclarationSyntax> {
      public IEnumerator<TypeDeclarationSyntax> GetEnumerator() {
        yield return caseClass;
        if (companion != null) yield return companion;
      }

      IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
      }
    }

    readonly struct FieldOrProp {
      public readonly string type;
      public readonly ITypeSymbol typeInfo;
      public readonly string identifier;
      public readonly string identifierFirstLetterUpper;
      public readonly bool initialized;
      public readonly bool traversable;

      static readonly string stringName = "string";
      static readonly string iEnumName = typeof(IEnumerable<>).FullName!;

      public FieldOrProp(
        ITypeSymbol typeInfo, string identifier, bool initialized, SemanticModel model
      ) {
        type = typeInfo.ToDisplayString();
        this.typeInfo = typeInfo;
        this.identifier = identifier;
        identifierFirstLetterUpper = identifier.FirstLetterToUpper();
        this.initialized = initialized;

        bool interfaceInIEnumerable(INamedTypeSymbol info) {
          return info.ContainingNamespace + "." + info.Name + "`" + info.Arity == iEnumName;
        }

        var typeName = typeInfo.ToDisplayString();

        var typeIsIEnumerableItself = typeInfo is INamedTypeSymbol ti && interfaceInIEnumerable(ti);
        var typeImplementsIEnumerable = typeInfo.AllInterfaces.Any(interfaceInIEnumerable);

        traversable =
          typeName != stringName && (typeIsIEnumerableItself || typeImplementsIEnumerable);
      }
    }
  }
}
