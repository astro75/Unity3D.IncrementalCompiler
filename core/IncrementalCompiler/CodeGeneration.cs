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
using MonadLib;
using static System.String;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IncrementalCompiler
{
    public static class GeneratedConstructorExts
    {
        public static bool generateConstructor(this GeneratedConstructor gc) {
            switch (gc)
            {
                case GeneratedConstructor.None:
                    return false;
                case GeneratedConstructor.Constructor:
                case GeneratedConstructor.ConstructorAndApply:
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(gc), gc, null);
            }
        }
    }

    public static partial class CodeGeneration
    {
        public const string GENERATED_FOLDER = "Generated";
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

        public static (CSharpCompilation, List<Diagnostic>) Run(
            bool incrementalRun,
            CSharpCompilation compilation,
            ImmutableArray<SyntaxTree> trees,
            CSharpParseOptions parseOptions,
            string assemblyName,
            ref GeneratedFilesMapping filesMapping,
            Dictionary<string, SyntaxTree> sourceMap
        ) {
            var generatedProjectFilesDirectory = Path.Combine(GENERATED_FOLDER, assemblyName);
            if (!incrementalRun && Directory.Exists(generatedProjectFilesDirectory))
            {
                DeleteFilesAndFoldersRecursively(generatedProjectFilesDirectory);
            }
            Directory.CreateDirectory(generatedProjectFilesDirectory);
            var oldCompilation = compilation;
            var diagnostic = new List<Diagnostic>();

            void tryAttribute<A>(AttributeData attr, Action<A> a) where A : Attribute {
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

            var results = trees.AsParallel().SelectMany(originalTree =>
            {
                var tree = originalTree;

                var model = oldCompilation.GetSemanticModel(tree);
                var root = tree.GetCompilationUnitRoot();
                var newMembers = ImmutableList<MemberDeclarationSyntax>.Empty;
                var typesInFile = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
                var result = ImmutableList<IGenerationResult>.Empty;

                var treeEdited = false;
                var editsList = new List<(SyntaxNode, SyntaxNode)>();
                void replaceSyntax(SyntaxNode oldNode, SyntaxNode newNode) {
                    treeEdited = true;
                    editsList.Add((oldNode, newNode));
                }

                foreach (var tds in typesInFile)
                {
                    bool treeContains(SyntaxReference syntaxRef) =>
                        tree == syntaxRef.SyntaxTree && tds.Span.Contains(syntaxRef.Span);

                    var symbol = model.GetDeclaredSymbol(tds);
                    JavaClassFile javaClassFile = null;
                    foreach (var attr in symbol.GetAttributes()) {
                        if (!treeContains(attr.ApplicationSyntaxReference)) continue;
                        var attrClassName = attr.AttributeClass.ToDisplayString();
                        if (attrClassName == caseType.FullName)
                        {
                            tryAttribute<RecordAttribute>(attr, instance => {
                                newMembers = newMembers.AddRange(
                                    GenerateCaseClass(instance, model, tds)
                                    .Select(generatedClass =>
                                        AddAncestors(tds, generatedClass, onlyNamespace: false)
                                    )
                                );
                            });
                        }
                        if (attrClassName == typeof(SingletonAttribute).FullName)
                        {
                            if (tds is ClassDeclarationSyntax cds) {
                                tryAttribute<SingletonAttribute>(attr, m =>
                                {
                                    newMembers = newMembers.Add(
                                        AddAncestors(tds, GenerateSingleton(cds), onlyNamespace: false)
                                    );
                                });
                            }
                        }
                        if (attrClassName == typeof(MatcherAttribute).FullName)
                        {
                            tryAttribute<MatcherAttribute>(attr, m => {
                                newMembers = newMembers.Add(
                                    AddAncestors(tds, GenerateMatcher(model, tds, m, typesInFile), onlyNamespace: true)
                                );
                            });
                        }
                        if (attrClassName == typeof(JavaClassAttribute).FullName)
                        {
                            tryAttribute<JavaClassAttribute>(attr, instance =>
                            {
                                javaClassFile = new JavaClassFile(symbol, module: instance.Module, imports: instance.Imports, classBody: instance.ClassBody, attrLocation(attr));
                                newMembers = newMembers.Add(AddAncestors(
                                    tds,
                                    CreatePartial(tds, javaClassFile.GenerateMembers(), Extensions.EmptyBaseList),
                                    onlyNamespace: false
                                 ));
                            });
                        }
                        if (attrClassName == typeof(JavaListenerInterfaceAttribute).FullName)
                        {
                            tryAttribute<JavaListenerInterfaceAttribute>(attr, instance =>
                            {
                                var javaInterface = new JavaClassFile(symbol, module: instance.Module, imports: "", classBody: "", attrLocation(attr));
                                result = result.Add(new GeneratedJavaFile(
                                    sourcePath: tree.FilePath,
                                    location: attrLocation(attr),
                                    javaFile: new JavaFile(
                                        module: javaInterface.Module,
                                        path: javaInterface.JavaFilePath(),
                                        contents: javaInterface.GenerateJavaInterface()
                                    )
                                ));
                                newMembers = newMembers.Add(AddAncestors(tds, javaInterface.GetInterfaceClass(), onlyNamespace: false));
                            });
                        }
                    }

                    var newClassMembers = ImmutableArray<string>.Empty;
                    foreach (var member in symbol.GetMembers())
                    {
                        switch (member)
                        {
                            case IFieldSymbol fieldSymbol:
                                foreach (var attr in fieldSymbol.GetAttributes())
                                {
                                    if (!treeContains(attr.ApplicationSyntaxReference)) continue;
                                    var attrClassName = attr.AttributeClass.ToDisplayString();
                                    if (attrClassName == typeof(PublicAccessor).FullName)
                                    {
                                        tryAttribute<PublicAccessor>(attr, _ =>
                                        {
                                            newClassMembers = newClassMembers.Add(GenerateAccessor(fieldSymbol, model));
                                        });
                                    }
                                    // TODO: generic way to add new attributes
                                    if (attrClassName == typeof(ThreadStaticAttribute).FullName)
                                    {
                                        tryAttribute<ThreadStaticAttribute>(attr, _ => throw new Exception($"Can't use {nameof(ThreadStaticAttribute)} in Unity"));
                                    }
                                }
                                break;
                            case IMethodSymbol methodSymbol:
                                foreach (var attr in methodSymbol.GetAttributes())
                                {
                                    if (!treeContains(attr.ApplicationSyntaxReference)) continue;
                                    var attrClassName = attr.AttributeClass.ToDisplayString();
                                    if (attrClassName == typeof(JavaMethodAttribute).FullName)
                                    {
                                        tryAttribute<JavaMethodAttribute>(attr, instance =>
                                        {
                                            if (javaClassFile == null) throw new Exception(
                                                $"must be used together with {nameof(JavaClassAttribute)}"
                                            );
                                            javaClassFile.AddMethod(instance.MethodBody, methodSymbol);
                                            var syntaxes = methodSymbol.DeclaringSyntaxReferences;
                                            if (syntaxes.Length != 1) throw new Exception($"code must be in one place");
                                            var syntax = (BaseMethodDeclarationSyntax) syntaxes[0].GetSyntax();
                                            var replacedSyntax = javaClassFile.GenerateMethod(methodSymbol, syntax);
                                            replaceSyntax(syntax, replacedSyntax);
                                        });
                                    }
                                }
                                break;
                        }
                    }

                    if (newClassMembers.Length > 0)
                    {
                        newMembers = newMembers.Add(AddAncestors(
                            tds,
                            CreatePartial(tds, ParseClassMembers(Join("\n", newClassMembers)), Extensions.EmptyBaseList),
                            onlyNamespace: false
                        ));
                    }

                    if (javaClassFile != null)
                    {
                        result = result.Add(new GeneratedJavaFile(
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
                    result = result.Add(new ModifiedFile(tree, newRoot));
                }
                if (newMembers.Any())
                {
                    var nt = CSharpSyntaxTree.Create(
                        SF.CompilationUnit()
                            .WithUsings(cleanUsings(root.Usings))
                            .WithLeadingTrivia(SyntaxTriviaList.Create(SyntaxFactory.Comment("// ReSharper disable all")))
                            .WithMembers(SF.List(newMembers))
                            .NormalizeWhitespace(),
                        path: Path.Combine(generatedProjectFilesDirectory, tree.FilePath),
                        options: parseOptions,
                        encoding: Encoding.UTF8);
                    result = result.Add(new GeneratedCsFile(sourcePath: tree.FilePath, tree: nt, location: root.GetLocation()));
                }
                return result.ToArray();
            }).ToArray();
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
                compilation, sourceMap, results.OfType<ModifiedFile>().Select(f => (f.From, f.To))
            );
            compilation = filesMapping.updateCompilation(compilation, parseOptions, assemblyName: assemblyName, generatedFilesDir: generatedProjectFilesDirectory);
            File.WriteAllLines(
                Path.Combine(GENERATED_FOLDER, $"Generated-files-{assemblyName}.txt"),
                filesMapping.filesDict.Values
                    .SelectMany(_ => _)
                    .Select(path => path.Replace("/", "\\")));
            return (compilation, diagnostic);
        }

        static Location attrLocation(AttributeData attr) => attr.ApplicationSyntaxReference.GetSyntax().GetLocation();

        static void SetNamedArguments(Type type, AttributeData attributeData, Attribute instance) {
            foreach (var arg in attributeData.NamedArguments)
            {
                // if some arguments are invalid they do not appear in NamedArguments list
                // because of that we do not check for errors
                var prop = type.GetProperty(arg.Key);
                prop.SetValue(instance, arg.Value.Value);
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
            MatcherAttribute attribute, ImmutableArray<TypeDeclarationSyntax> typesInFile
        ) {
            // TODO: ban extending this class in different files
            // TODO: generics ?

            var baseTypeSymbol = model.GetDeclaredSymbol(tds);
            var symbols = typesInFile
                .Select(t => model.GetDeclaredSymbol(t))
                // move current symbol to back
                .OrderBy(s => s.Equals(baseTypeSymbol));

            IEnumerable<INamedTypeSymbol> findTypes() { switch (tds) {
                case ClassDeclarationSyntax _:
                    return symbols.Where(s => {
                        if (!baseTypeSymbol.IsAbstract && s.Equals(baseTypeSymbol)) return true;
                        return s.BaseType?.Equals(baseTypeSymbol) ?? false;
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
            readonly Maybe<TypeDeclarationSyntax> companion;

            public CaseClass(TypeDeclarationSyntax caseClass, Maybe<TypeDeclarationSyntax> companion) {
                this.caseClass = caseClass;
                this.companion = companion;
            }

            public IEnumerator<TypeDeclarationSyntax> GetEnumerator() {
                yield return caseClass;
                foreach (var c in companion.ToEnumerable()) yield return c;
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        struct FieldOrProp {
            public readonly TypeSyntax type;
            public readonly SyntaxToken identifier;
            public readonly string identifierFirstLetterUpper;
            public readonly bool initialized;
            public readonly bool traversable;

            static readonly string stringName = "string";
            static readonly string iEnumName = typeof(IEnumerable<>).FullName;

            public FieldOrProp(
                TypeSyntax type, SyntaxToken identifier, bool initialized, SemanticModel model
            ) {
                this.type = type;
                this.identifier = identifier;
                identifierFirstLetterUpper = identifier.Text.firstLetterToUpper();
                this.initialized = initialized;

                bool interfaceInIEnumerable(INamedTypeSymbol info) =>
                    info.ContainingNamespace + "." + info.Name + "`" + info.Arity == iEnumName;

                var typeInfo = model.GetTypeInfo(type).Type;
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
            .tap(_ => Join(", ", _));

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
            TypeDeclarationSyntax originalType, IEnumerable<MemberDeclarationSyntax> newMembers, BaseListSyntax baseList
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
            SyntaxKind kind, SyntaxToken identifier, SyntaxTokenList modifiers, TypeParameterListSyntax typeParams,
            SyntaxList<MemberDeclarationSyntax> members, BaseListSyntax baseList
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
