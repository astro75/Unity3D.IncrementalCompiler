using System;
using System.Collections;
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

namespace IncrementalCompiler
{
    public class GenerationSettings
    {
        public readonly string partialsFolder;
        public readonly string macrosFolder;
        public readonly string? txtForPartials;
        public readonly string baseDirectory;

        readonly Uri baseDirectoryUri;

        public GenerationSettings(string partialsFolder, string macrosFolder, string? txtForPartials, string baseDirectory) {
            this.partialsFolder = partialsFolder;
            this.macrosFolder = macrosFolder;
            this.txtForPartials = txtForPartials;
            this.baseDirectory = baseDirectory;
            baseDirectoryUri = new Uri(Path.GetFullPath(baseDirectory));
        }

        public string getRelativePath(string path) =>
            baseDirectoryUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString();
    }

    class GeneratorCtx
    {
        public readonly List<MemberDeclarationSyntax> NewMembers = new List<MemberDeclarationSyntax>();
        public readonly SemanticModel Model;
        public readonly ImmutableArray<TypeDeclarationSyntax> TypesInFile;
        public readonly List<INamedTypeSymbol> TypesWithMacros = new List<INamedTypeSymbol>();

        public GeneratorCtx(CompilationUnitSyntax root, SemanticModel model) {
            Model = model;
            TypesInFile = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        }

        public void AddMacroMethod(INamedTypeSymbol symbol) {
            TypesWithMacros.Add(symbol);
        }
    }

    public static partial class CodeGeneration
    {
        static readonly Type caseType = typeof(RecordAttribute);
        static readonly HashSet<SyntaxKind> kindsForExtensionClass = new HashSet<SyntaxKind>(new[] {
            SyntaxKind.PublicKeyword, SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword
        });

        public partial class GeneratedFilesMapping
        {
            public Dictionary<string, List<string>> filesDict = new Dictionary<string, List<string>>();

            static void addValue<A>(Dictionary<string, List<A>> dict, string key, A value) {
                if (!dict.ContainsKey(key)) dict[key] = new List<A>();
                dict[key].Add(value);
            }

            static IEnumerable<A> enumerate<A>(Dictionary<string, List<A>> dict) =>
                dict.Values.SelectMany(_ => _);

            public void add(string key, string value) => addValue(filesDict, key, value);

            static string asVerbatimString(string str) => $"@\"{str.Replace("\"", "\"\"")}\"";


            public void removeFiles(IEnumerable<string> filesToRemove) {
                foreach (var filePath in filesToRemove) {
                    if (filesDict.TryGetValue(filePath, out var generatedFiles)) {
                        foreach (var generatedFile in generatedFiles) {
                            if (File.Exists(generatedFile)) File.Delete(generatedFile);
                        }
                        filesDict.Remove(filePath);
                    }

                    maybeRemoveJavaFile(filePath);
                }
            }
        }

        interface IGenerationResult {}

        class ModifiedFile : IGenerationResult
        {
            public readonly SyntaxTree From;
            public readonly CompilationUnitSyntax To;

            public ModifiedFile(SyntaxTree from, CompilationUnitSyntax to) {
                From = from;
                To = to;
            }
        }

        abstract class GeneratedFile : IGenerationResult
        {
            public readonly string SourcePath;
            public readonly Location Location;

            protected GeneratedFile(string sourcePath, Location location) {
                SourcePath = sourcePath;
                Location = location;
            }
        }


        class GeneratedCsFile : GeneratedFile
        {
            public readonly SyntaxTree Tree;
            public readonly string FilePath, Contents;

            public GeneratedCsFile(string sourcePath, Location location, SyntaxTree tree) : base(sourcePath, location) {
                Tree = tree;
                FilePath = tree.FilePath;
                Contents = tree.GetText().ToString();
            }
        }

        static string Block(IEnumerable<string> contents) => "{" + Join("\n", contents) + "}";
        static string Block(params string[] contents) => Block((IEnumerable<string>)contents);

        // Directory.Delete(recursive: true) throws an exception in some cases
        static void DeleteFilesAndFoldersRecursively(string targetDir)
        {
            foreach (var file in Directory.GetFiles(targetDir))
                File.Delete(file);

            foreach (var subDir in Directory.GetDirectories(targetDir))
                DeleteFilesAndFoldersRecursively(subDir);

            try
            {
                Directory.Delete(targetDir);
            }
            catch (IOException)
            {
                // it fails one time if Windows file explorer is opened in targetDir
                try
                {
                    Directory.Delete(targetDir);
                }
                catch (DirectoryNotFoundException)
                {
                    // we get this error on mac sometimes
                }
            }
        }

        public static void DeleteFilesRecursively(string targetDir)
        {
            foreach (var file in Directory.GetFiles(targetDir)) File.Delete(file);
            foreach (var subDir in Directory.GetDirectories(targetDir)) DeleteFilesRecursively(subDir);
        }

        public static void tryAttribute<A>(AttributeData attr, Action<A> a, List<Diagnostic> diagnostic) where A : Attribute {
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
                ), attrLocation(attr)));
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
            GenerationSettings settings
        ) {
            var oldCompilation = compilation;
            var diagnostic = new List<Diagnostic>();

            void tryAttributeLocal<A>(AttributeData attr, Action<A> a) where A : Attribute =>
                tryAttribute(attr, a, diagnostic);

            var typeAttributes = new Dictionary<
                INamedTypeSymbol,
                Action<AttributeData, GeneratorCtx, TypeDeclarationSyntax, INamedTypeSymbol>
            >();

            var methodAttributes = new Dictionary<
                INamedTypeSymbol,
                Action<AttributeData, GeneratorCtx, TypeDeclarationSyntax, IMethodSymbol>
            >();

            {
                addAttribute<RecordAttribute>((instance, ctx, tds, symbol) =>
                {
                    ctx.NewMembers.AddRange(
                        GenerateCaseClass(instance, ctx.Model, tds, symbol)
                            .Select(generatedClass => AddAncestors(tds, generatedClass, onlyNamespace: false))
                    );
                });

                addAttribute<SingletonAttribute>((instance, ctx, tds, symbol) =>
                {
                    if (tds is ClassDeclarationSyntax cds) {
                        ctx.NewMembers.Add(
                            AddAncestors(tds, GenerateSingleton(cds), onlyNamespace: false)
                        );
                    }
                    else
                    {
                        throw new Exception("Can only be used on a class");
                    }
                });

                addAttribute<MatcherAttribute>((instance, ctx, tds, symbol) =>
                {
                    var matcher = GenerateMatcher(ctx.Model, tds, instance, ctx.TypesInFile, symbol);
                    ctx.NewMembers.Add(AddAncestors(tds, matcher, onlyNamespace: true));
                });

                addMacroAttribute<SimpleMethodMacro>();
                addMacroAttribute<StatementMethodMacro>();
                addMacroAttribute<VarMethodMacro>();

                void addAttribute<A>(Action<A, GeneratorCtx, TypeDeclarationSyntax, INamedTypeSymbol> act) where A : Attribute {
                    var compilationType = compilation.GetTypeByMetadataName(typeof(A).FullName);
                    if (compilationType != null)
                    {
                        typeAttributes.Add(
                            compilationType,
                            (attr, ctx, tds, symbol) => tryAttributeLocal<A>(attr, instance => act(instance, ctx, tds, symbol))
                        );
                    }
                }

                void addMacroAttribute<A>() {
                    var compilationType = compilation.GetTypeByMetadataName(typeof(A).FullName);
                    if (compilationType != null)
                    {
                        methodAttributes.Add(
                            compilationType,
                            (attr, ctx, tds, symbol) => ctx.AddMacroMethod(symbol.ContainingType)
                        );
                    }
                }
            }

            var resultsFromTrees = trees.AsParallel().Select(originalTree =>
            {
                var tree = originalTree;

                var model = oldCompilation.GetSemanticModel(tree);
                var root = tree.GetCompilationUnitRoot();
                var results = new List<IGenerationResult>();

                var treeEdited = false;
                var editsList = new List<(SyntaxNode, SyntaxNode)>();
                // void replaceSyntax(SyntaxNode oldNode, SyntaxNode newNode) {
                //     treeEdited = true;
                //     editsList.Add((oldNode, newNode));
                // }

                var ctx = new GeneratorCtx(root, model);

                foreach (var tds in ctx.TypesInFile)
                {
                    var symbol = model.GetDeclaredSymbol(tds);
                    if (symbol == null) continue;
                    JavaClassFile? javaClassFile = null;
                    foreach (var attr in symbol.GetAttributes()) {
                        if (!treeContains(attr.ApplicationSyntaxReference, tree, tds)) continue;
                        if (attr.AttributeClass == null) continue;

                        if (typeAttributes.TryGetValue(attr.AttributeClass, out var action))
                        {
                            action(attr, ctx, tds, symbol);
                        }

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
                    {
                        switch (member)
                        {
                            case IFieldSymbol fieldSymbol:
                                foreach (var attr in fieldSymbol.GetAttributes())
                                {
                                    if (!treeContains(attr.ApplicationSyntaxReference, tree, tds)) continue;
                                    if (attr.AttributeClass == null) continue;
                                    var attrClassName = attr.AttributeClass.ToDisplayString();
                                    if (attrClassName == typeof(PublicAccessor).FullName)
                                    {
                                        tryAttributeLocal<PublicAccessor>(attr, _ =>
                                        {
                                            newClassMembers = newClassMembers.Add(GenerateAccessor(fieldSymbol, model));
                                        });
                                    }
                                    // TODO: generic way to add new attributes
                                    if (attrClassName == typeof(ThreadStaticAttribute).FullName)
                                    {
                                        tryAttributeLocal<ThreadStaticAttribute>(attr, _ =>
                                            throw new Exception($"Can't use {nameof(ThreadStaticAttribute)} in Unity"
                                        ));
                                    }
                                }
                                break;
                            case IMethodSymbol methodSymbol:
                                foreach (var attr in methodSymbol.GetAttributes())
                                {
                                    if (!treeContains(attr.ApplicationSyntaxReference, tree, tds)) continue;
                                    if (attr.AttributeClass == null) continue;
                                    if (methodAttributes.TryGetValue(attr.AttributeClass, out var action))
                                    {
                                        action(attr, ctx, tds, methodSymbol);
                                    }
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
                                break;
                        }
                    }

                    if (newClassMembers.Length > 0)
                    {
                        ctx.NewMembers.Add(AddAncestors(
                            tds,
                            CreatePartial(tds, ParseClassMembers(Join("\n", newClassMembers)), Extensions.EmptyBaseList),
                            onlyNamespace: false
                        ));
                    }

                    if (javaClassFile != null)
                    {
                        results.Add(new GeneratedJavaFile(
                            sourcePath: tree.FilePath,
                            location: javaClassFile.Location,
                            javaFile: new JavaFile(
                                module: javaClassFile.Module,
                                path: javaClassFile.JavaFilePath(),
                                contents: javaClassFile.GenerateJava()
                            )
                        ));
                    }
                }
                if (treeEdited)
                {
                    var newRoot = root.ReplaceNodes(
                        editsList.Select(t => t.Item1),
                        (toReplace, _) => editsList.First(t => t.Item1 == toReplace).Item2
                    );
                    results.Add(new ModifiedFile(tree, newRoot));
                }
                if (ctx.NewMembers.Count > 0)
                {
                    var treePath = settings.getRelativePath(tree.FilePath);
                    var nt = CSharpSyntaxTree.Create(
                        SF.CompilationUnit()
                            .WithUsings(cleanUsings(root.Usings))
                            .WithLeadingTrivia(SyntaxTriviaList.Create(SyntaxFactory.Comment("// ReSharper disable all")))
                            .WithMembers(SF.List(ctx.NewMembers))
                            .NormalizeWhitespace(),
                        path: Path.Combine(settings.partialsFolder, treePath),
                        options: parseOptions,
                        encoding: Encoding.UTF8);
                    results.Add(new GeneratedCsFile(sourcePath: treePath, tree: nt, location: root.GetLocation()));
                }
                return (results, ctx.TypesWithMacros);
            }).ToArray();

            var results = resultsFromTrees.SelectMany(_ => _.results).ToList();

            {
                var typesWithMacros = resultsFromTrees
                    .SelectMany(_ => _.TypesWithMacros)
                    .Distinct()
                    .OrderBy(_ => _.Name)
                    .ToArray();

                if (typesWithMacros.Length > 0)
                {
                    var typesString = Join(", ", typesWithMacros.Select(_ => $"typeof({_.ToDisplayString()})"));

                    var syntax = CSharpSyntaxTree.ParseText(
                        $"[assembly: {typeof(TypesWithMacroAttributes).FullName}({typesString})]"
                    ).GetCompilationUnitRoot();

                    var nt = CSharpSyntaxTree.Create(
                        syntax,
                        path: Path.Combine(settings.partialsFolder, "MacroList.cs"),
                        options: parseOptions,
                        encoding: Encoding.UTF8);

                    results.Add(new GeneratedCsFile(sourcePath: "MacroList.cs", tree: nt, location: Location.None));
                }
            }

            var csFiles = results.OfType<GeneratedCsFile>().ToArray();
            compilation = compilation.AddSyntaxTrees(csFiles.Select(_ => _.Tree));
            foreach (var file in csFiles)
            {
                sourceMap[file.FilePath] = file.Tree;
                var generatedPath = file.FilePath;
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
                    filesMapping.add(file.SourcePath, generatedPath);
                }
            }
            foreach (var file in results.OfType<GeneratedJavaFile>())
            {
                if (!filesMapping.tryAddJavaFile(file.SourcePath, file.JavaFile))
                {
                    diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
                        "ER0002", "Error", $"Could not generate java file '{file.JavaFile.Path}'. File already exists.", "Error", DiagnosticSeverity.Error, true
                    ), file.Location));
                }
            }

            compilation = MacroProcessor.EditTrees(
                compilation, sourceMap, results.OfType<ModifiedFile>().Select(f => (f.From, f.To)), settings
            );
            compilation = filesMapping.updateCompilation(compilation, parseOptions, assemblyName: assemblyName, generatedFilesDir: settings.partialsFolder);
            if (settings.txtForPartials != null)
            {
                File.WriteAllLines(
                    settings.txtForPartials,
                    filesMapping.filesDict.Values
                        .SelectMany(_ => _)
                        .Select(path => path.Replace("/", "\\")));
            }
            return (compilation, diagnostic);
        }

        static IEnumerable<A> NullableAsEnumerable<A>(A? maybeValue) where A : class =>
            maybeValue != null ? new[] {maybeValue} : Enumerable.Empty<A>();

        static bool treeContains(SyntaxReference? syntaxRef, SyntaxTree tree, TypeDeclarationSyntax tds) {
            return tree == syntaxRef?.SyntaxTree
                   &&
                   tds.Span.Contains(syntaxRef.Span);
        }

        static Location attrLocation(AttributeData attr) => attr.ApplicationSyntaxReference!.GetSyntax().GetLocation();

        static void SetNamedArguments(Type type, AttributeData attributeData, Attribute instance) {
            foreach (var arg in attributeData.NamedArguments)
            {
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

        // CSharpErrorMessageFormat is default for ToDisplayString
        static readonly SymbolDisplayFormat format =
            SymbolDisplayFormat
            .CSharpErrorMessageFormat
            .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included);

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

            IEnumerable<INamedTypeSymbol> findTypes() { switch (tds) {
                case ClassDeclarationSyntax _:
                    return symbols.Where(s => {
                        if (!baseTypeSymbol.IsAbstract && SymbolEqualityComparer.Default.Equals(s, baseTypeSymbol)) return true;
                        return SymbolEqualityComparer.Default.Equals(s.BaseType, baseTypeSymbol);
                    });
                case InterfaceDeclarationSyntax _:
                    return symbols.Where(s => s.Interfaces.Contains(baseTypeSymbol));
                default:
                    throw new Exception($"{tds} - matcher should be added on class or interface");
            } }

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

            string toLowerFirstLetter(string s) => char.ToLowerInvariant(s[0]) + s.Substring(1);

            var childNames = childTypes.Select(s => (fullName: s.ToString(), varName: toLowerFirstLetter(s.Name))).ToArray();

            var firstParam = new[]{$"this {baseTypeSymbol} obj"};

            string VoidMatch()
            {
                var parameters = Join(", ", firstParam.Concat(childNames.Select(t => $"System.Action<{t.fullName}> {t.varName}")));
                var body = Join("\n", childNames.Select(t =>
                  $"var val_{t.varName} = obj as {t.fullName};" +
                  $"if (val_{t.varName} != null) {{ {t.varName}(val_{t.varName}); return; }}"));

                return $"public static void voidMatch({parameters}) {{{body}}}";
            }

            string Match()
            {
                var parameters = Join(", ", firstParam.Concat(childNames.Select(t => $"System.Func<{t.fullName}, A> {t.varName}")));
                var body = Join("\n", childNames.Select(t =>
                    $"var val_{t.varName} = obj as {t.fullName};" +
                    $"if (val_{t.varName} != null) return {t.varName}(val_{t.varName});"));

                return $"public static A match<A>({parameters}) {{" +
                       $"{body}" +
                       $"throw new System.ArgumentOutOfRangeException(\"obj\", obj, \"Should never reach this\");" +
                       $"}}";
            }

            var className = IsNullOrWhiteSpace(attribute.ClassName)
                ? tds.Identifier + "Matcher"
                : attribute.ClassName;
            return CreateStatic(className, tds, ParseClassMembers(VoidMatch() + Match()));
        }

        public class CaseClass : IEnumerable<TypeDeclarationSyntax> {
            readonly TypeDeclarationSyntax caseClass;
            readonly TypeDeclarationSyntax? companion;

            public CaseClass(TypeDeclarationSyntax caseClass, TypeDeclarationSyntax? companion) {
                this.caseClass = caseClass;
                this.companion = companion;
            }

            public IEnumerator<TypeDeclarationSyntax> GetEnumerator() {
                yield return caseClass;
                if (companion != null) yield return companion;
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        struct FieldOrProp {
            public readonly TypeSyntax type;
            public readonly ITypeSymbol typeInfo;
            public readonly SyntaxToken identifier;
            public readonly string identifierFirstLetterUpper;
            public readonly bool initialized;
            public readonly bool traversable;

            static readonly string stringName = "string";
            static readonly string iEnumName = typeof(IEnumerable<>).FullName!;

            public FieldOrProp(
                TypeSyntax type, SyntaxToken identifier, bool initialized, SemanticModel model
            ) {
                this.type = type;
                this.identifier = identifier;
                identifierFirstLetterUpper = identifier.Text.FirstLetterToUpper();
                this.initialized = initialized;

                bool interfaceInIEnumerable(INamedTypeSymbol info) =>
                    info.ContainingNamespace + "." + info.Name + "`" + info.Arity == iEnumName;

                var typeInfoLocal = model.GetTypeInfo(type).Type;
                typeInfo = typeInfoLocal ?? throw new Exception($"Type info not found for {identifier}");
                var typeName = typeInfo.ToDisplayString();

                var typeIsIEnumerableItself = typeInfo is INamedTypeSymbol ti && interfaceInIEnumerable(ti);
                var typeImplementsIEnumerable = typeInfo.AllInterfaces.Any(interfaceInIEnumerable);

                traversable =
                    typeName != stringName && (typeIsIEnumerableItself || typeImplementsIEnumerable);
            }
        }

        static string joinCommaSeparated<A>(this IEnumerable<A> collection, Func<A, string> mapper) =>
            collection
            .Select(mapper)
            .Tap(_ => Join(", ", _));

        static SyntaxList<MemberDeclarationSyntax> GenerateStaticApply(
            TypeDeclarationSyntax cds, ICollection<FieldOrProp> props
        ) {
            var genericArgsStr = cds.TypeParameterList?.ToFullString().TrimEnd() ?? "";
            var funcParamsStr = joinCommaSeparated(props, p => p.type + " " + p.identifier.ValueText);
            var funcArgs = joinCommaSeparated(props, p => p.identifier.ValueText);

            return ParseClassMembers(
                $"public static {cds.Identifier.ValueText}{genericArgsStr} a{genericArgsStr}" +
                $"({funcParamsStr}) => new {cds.Identifier.ValueText}{genericArgsStr}({funcArgs});"
            );
        }

        private static TypeDeclarationSyntax CreatePartial(
            TypeDeclarationSyntax originalType, IEnumerable<MemberDeclarationSyntax> newMembers, BaseListSyntax? baseList
        ) =>
            CreateType(
                originalType.Kind(),
                originalType.Identifier,
                originalType.Modifiers.Add(SyntaxKind.PartialKeyword),
                originalType.TypeParameterList,
                SF.List(newMembers),
                baseList
            );

        private static TypeDeclarationSyntax CreateStatic(
            string className,
            TypeDeclarationSyntax originalType,
            IEnumerable<MemberDeclarationSyntax> newMembers
        ) =>
            SF.ClassDeclaration(className)
            .WithModifiers(SF
                .TokenList(originalType.Modifiers.Where(k => kindsForExtensionClass.Contains(k.Kind())))
                .Add(SyntaxKind.StaticKeyword))
            .WithMembers(SF.List(newMembers));

        public static TypeDeclarationSyntax CreateType(
            SyntaxKind kind, SyntaxToken identifier, SyntaxTokenList modifiers, TypeParameterListSyntax? typeParams,
            SyntaxList<MemberDeclarationSyntax> members, BaseListSyntax? baseList
        ) {
            switch (kind)
            {
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
        /// Copies namespace and class hierarchy from the original <see cref="memberNode"/>
        /// </summary>
        // stolen from CodeGeneration.Roslyn
        static MemberDeclarationSyntax AddAncestors(
            MemberDeclarationSyntax memberNode, MemberDeclarationSyntax generatedType, bool onlyNamespace
        )
        {
            // Figure out ancestry for the generated type, including nesting types and namespaces.
            foreach (var ancestor in memberNode.Ancestors())
            {
                switch (ancestor)
                {
                    case NamespaceDeclarationSyntax a:
                        generatedType =
                            SF.NamespaceDeclaration(a.Name)
                            .WithUsings(cleanUsings(a.Usings))
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
            }
            return generatedType;
        }

        static SyntaxList<UsingDirectiveSyntax> cleanUsings(SyntaxList<UsingDirectiveSyntax> usings) =>
            SF.List(usings.Select(u =>
                u.WithUsingKeyword(u.UsingKeyword.WithoutTrivia())
            ));

        static ClassDeclarationSyntax ParseClass(string syntax)
        {
            var cls = (ClassDeclarationSyntax)CSharpSyntaxTree.ParseText(syntax).GetCompilationUnitRoot().Members[0];
            return cls;
        }

        static SyntaxList<MemberDeclarationSyntax> ParseClassMembers(string syntax)
        {
            var cls = (ClassDeclarationSyntax)CSharpSyntaxTree.ParseText($"class C {{ {syntax} }}").GetCompilationUnitRoot().Members[0];
            return cls.Members;
        }

        static BlockSyntax ParseBlock(string syntax) {
            return (BlockSyntax) SF.ParseStatement("{" + syntax + "}");
        }

        public static string Quote(string s) => $"\"{s}\"";
    }
}
