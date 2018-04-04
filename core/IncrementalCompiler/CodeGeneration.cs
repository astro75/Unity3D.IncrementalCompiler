using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GenerationAttributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static System.String;

namespace IncrementalCompiler
{
    public static class CodeGeneration
    {
        public const string GENERATED_FOLDER = "Generated";
        static readonly Type caseType = typeof(RecordAttribute);
        static readonly HashSet<SyntaxKind> kindsForExtensionClass = new HashSet<SyntaxKind>(new[] {
            SyntaxKind.PublicKeyword, SyntaxKind.InternalKeyword, SyntaxKind.PrivateKeyword
        });

        public class GeneratedFilesMapping
        {
            public Dictionary<string, List<string>> dict = new Dictionary<string, List<string>>();

            public void add(string key, string value) {
                if (!dict.ContainsKey(key)) dict[key] = new List<string>();
                dict[key].Add(value);
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

        class GeneratedFile : IGenerationResult
        {
            public readonly string SourcePath, FilePath, Contents;
            public readonly Location location;

            public GeneratedFile(string sourcePath, string filePath, string contents, Location location) {
                SourcePath = sourcePath;
                FilePath = filePath;
                Contents = contents;
                this.location = location;
            }
        }

        class GeneratedCsFile : GeneratedFile
        {
            public readonly SyntaxTree Tree;
            public GeneratedCsFile(string sourcePath, SyntaxTree tree, Location location) : base(sourcePath, tree.FilePath, tree.GetText().ToString(), location) {
                Tree = tree;
            }
        }

        // TODO: clean this class
        // refactor parts to tlplib
        class JavaClassFile
        {
            public readonly Location Location;
            readonly string Module;
            readonly string ClassBody;
            readonly INamedTypeSymbol Symbol;
            readonly List<string> Methods = new List<string>();
            string Package => "com.generated." + Module;
            public string PackageWithClass => Package + "." + Symbol.Name;
            readonly IMethodSymbol[] allMethods;

            public JavaClassFile(INamedTypeSymbol symbol, string module, string classBody, Location location) {
                Symbol = symbol;
                Module = module;
                ClassBody = classBody;
                allMethods = AllInterfaceMembers(symbol).OfType<IMethodSymbol>().ToArray();
                Location = location;
            }

            static ImmutableArray<ISymbol> AllInterfaceMembers(INamedTypeSymbol symbol) =>
                symbol.GetMembers().AddRange(symbol.AllInterfaces.SelectMany(i => i.GetMembers()));

            public void AddMethod(string methodBody, IMethodSymbol methodSymbol) {
                var modifier = methodSymbol.IsStatic ? "static " : "";
                var parameters = Join(", ",
                    methodSymbol.Parameters.Select(p => $"final {ToJavaType(p.Type)} {p.Name}").ToArray());
                Methods.Add(
                    $"public {modifier}{ToJavaType(methodSymbol.ReturnType)} {methodSymbol.Name}({parameters}) {{\n" +
                    $"{methodBody}\n" +
                    "}\n"
                );
            }

            public string GenerateJava() {
                var modifier = Symbol.IsStatic ? "static " : "";
                return $"package {Package};\n\n" +
                       $"public {modifier}class {Symbol.Name} {{\n" +
                       $"{ClassBody}\n" +
                       Join("\n", Methods) +
                       "}";
            }

            public IEnumerable<MemberDeclarationSyntax> GenerateMembers() {
                return ParseClassMembers(
                    $"static UnityEngine.AndroidJavaClass jc = new UnityEngine.AndroidJavaClass(\"{PackageWithClass}\");"
                );
            }

            public MethodDeclarationSyntax GenerateMethod(IMethodSymbol symbol, MethodDeclarationSyntax syntax) {
                var isVoid = symbol.ReturnType.SpecialType == SpecialType.System_Void;
                var genericReturn = isVoid ? "" : "<" + syntax.ReturnType + ">";
                var returnStatement = isVoid ? "" : "return ";
                var callStetement = symbol.IsStatic ? "jc.CallStatic" : "jo.Call";
                var arguments = Join(", ", syntax.ParameterList.Parameters.Select(ps => ps.Identifier.ToString()));
                return syntax
                    .WithBody(ParseBlock($"{returnStatement}{callStetement}{genericReturn}({arguments});"))
                    .WithExpressionBody(null)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));
            }

            static string ToJavaType(ITypeSymbol type) {
                if (!type.IsDefinition) return ToJavaType(type.OriginalDefinition);
                switch (type.SpecialType) {
                    case SpecialType.System_String: return "String";
                    case SpecialType.System_Boolean: return "boolean";
                    case SpecialType.System_Byte: return "byte";
                    case SpecialType.System_Char: return "char";
                    case SpecialType.System_Int16: return "short";
                    case SpecialType.System_Int32: return "int";
                    case SpecialType.System_Int64: return "long";
                    case SpecialType.System_Single: return "float";
                    case SpecialType.System_Double: return "double";
                    case SpecialType.System_Void: return "void";
                    case SpecialType.System_Nullable_T:
                        var arguments = type.BaseType.TypeArguments;
                        if (arguments.Length == 0) break;
                        switch (type.BaseType.TypeArguments[0].SpecialType) {
                            case SpecialType.System_Boolean: return "Boolean";
                            case SpecialType.System_Byte:    return "Byte";
                            case SpecialType.System_Char:    return "Character";
                            case SpecialType.System_Int16:   return "Short";
                            case SpecialType.System_Int32:   return "Integer";
                            case SpecialType.System_Int64:   return "Long";
                            case SpecialType.System_Single:  return "Float";
                            case SpecialType.System_Double:  return "Double";
                        }
                        break;
                }
                throw new Exception($"Unsupported type: {type.ToDisplayString()}");
            }

            IEnumerable<string> InterfaceMethods() {
                return allMethods.Select(m =>
                {
                    var parameters = m.Parameters.Select(p => $"final {ToJavaType(p.Type)} {p.Name}");
                    return $"void {m.Name}({Join(", ", parameters)});";
                });
            }

            public string GenerateJavaInterface() =>
                $"package {Package}\n\n" +
                $"public interface {Symbol.Name} " + Block(
                    $"{ClassBody}",
                    Join("\n", InterfaceMethods())
                );

            public string JavaFilePath(string rootPath) => Path.Combine(
                rootPath,
                "android",
                Module,
                PackageWithClass.Replace('.', Path.DirectorySeparatorChar) + ".java"
            );

            public ClassDeclarationSyntax GetInterfaceClass() {
                return ParseClass(
                    $"public class {Symbol.Name}Proxy : com.tinylabproductions.TLPLib.Android.JavaListenerProxy" +
                    Block(
                        Join("\n", allMethods.Select(m =>
                        {
                            if (m.ReturnType.SpecialType != SpecialType.System_Void) throw new Exception("Return type must be void");
                            var parameterTypes = m.Parameters.Select(p => p.Type.ToString()).ToArray();
                            var genericArgs = parameterTypes.Length == 0 ? "" : $"<{Join(", ", parameterTypes)}>";
                            return $"public event System.Action{genericArgs} {m.Name};";
                        })),
                        $"{Symbol.Name}Proxy() : base(\"{PackageWithClass}\"){{}}" +
                            "protected override void invokeOnMain(string methodName, object[] args)" + Block(
                            "  switch(methodName)" + Block(
                                allMethods.Select(m => {
                                    var invokeParams = Join(", ", m.Parameters.Select((p, idx) => $"({p.Type.ToString()}) args[{idx}]"));
                                    return $"case \"{m.Name}\": {m.Name}?.Invoke({invokeParams}); return;";
                                })
                            ),
                            "base.invokeOnMain(methodName, args);"
                        ),
                        "public void registerLogger(string prefix, com.tinylabproductions.TLPLib.Logger.ILog log)" + Block(
                            allMethods.Select(m =>
                            {
                                var paramNames = m.Parameters.Select(p => p.Name).ToArray();
                                var paramsStr = paramNames.Length == 0 ? "\"\"" : Join(" + \", \" + ", paramNames.Select(p => $"{p}.ToString()"));
                                return $"{m.Name} += ({Join(", ", paramNames)}) => com.tinylabproductions.TLPLib.Logger.ILogExts.debug(log, prefix + \"{m.Name}(\" + {paramsStr} + \")\");";
                            })
                        )
                    )
                );
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
                // it fails one thime if Windows file explorer is opened in targetDir
                Directory.Delete(targetDir);
            }
        }

        public static (CSharpCompilation, ICollection<Diagnostic>) Run(
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
                        "ER0001", "Error", $"Compiler error for attribute {typeof(A).Name}: {e.Message}({e.Source}) at {e.StackTrace}", "Error", DiagnosticSeverity.Error, true
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
                    foreach (var attr in symbol.GetAttributes())
                    {
                        if (!treeContains(attr.ApplicationSyntaxReference)) continue;
                        var attrClassName = attr.AttributeClass.ToDisplayString();
                        if (attrClassName == caseType.FullName)
                        {
                            tryAttribute<RecordAttribute>(attr, instance => {
                                newMembers = newMembers.Add(AddAncestors(tds, GenerateCaseClass(instance, model, tds), onlyNamespace: false));
                            });
                        }
                        if (attrClassName == typeof(MatcherAttribute).FullName)
                        {
                            tryAttribute<MatcherAttribute>(attr, _ => {
                                newMembers = newMembers.Add(
                                    AddAncestors(tds, GenerateMatcher(model, tds, typesInFile), onlyNamespace: true)
                                );
                            });
                        }
                        if (attrClassName == typeof(JavaClassAttribute).FullName)
                        {
                            tryAttribute<JavaClassAttribute>(attr, instance =>
                            {
                                javaClassFile = new JavaClassFile(symbol, module: instance.Module, classBody: instance.ClassBody, attrLocation(attr));
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
                                var javaInterface = new JavaClassFile(symbol, module: instance.Module, classBody: "", attrLocation(attr));
                                result = result.Add(new GeneratedFile(
                                    sourcePath: tree.FilePath,
                                    filePath: javaInterface.JavaFilePath(generatedProjectFilesDirectory),
                                    contents: javaInterface.GenerateJavaInterface(),
                                    location: attrLocation(attr)
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
                                            newClassMembers = newClassMembers.Add(GenerateAccessor(fieldSymbol));
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
                                            var syntax = (MethodDeclarationSyntax) syntaxes[0].GetSyntax();
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
                        result = result.Add(new GeneratedFile(
                            sourcePath: tree.FilePath,
                            filePath: javaClassFile.JavaFilePath(generatedProjectFilesDirectory),
                            contents: javaClassFile.GenerateJava(),
                            javaClassFile.Location
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
                        SyntaxFactory.CompilationUnit()
                            .WithUsings(cleanUsings(root.Usings))
                            .WithMembers(SyntaxFactory.List(newMembers))
                            .NormalizeWhitespace(),
                        path: Path.Combine(generatedProjectFilesDirectory, tree.FilePath),
                        options: parseOptions,
                        encoding: Encoding.UTF8);
                    result = result.Add(new GeneratedCsFile(tree.FilePath, nt, root.GetLocation()));
                }
                return result.ToArray();
            }).ToArray();
            var newFiles = results.OfType<GeneratedFile>().ToArray();
            var csFiles = newFiles.OfType<GeneratedCsFile>().ToArray();
            compilation = compilation.AddSyntaxTrees(csFiles.Select(_ => _.Tree));
            foreach (var csFile in csFiles) sourceMap[csFile.FilePath] = csFile.Tree;
            foreach (var file in newFiles)
            {
                var generatedPath = file.FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(generatedPath));
                if (File.Exists(generatedPath))
                {
                    diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
                        "ER0002", "Error", $"Could not generate file '{generatedPath}'. File already exists.", "Error", DiagnosticSeverity.Error, true
                    ), file.location));
                }
                else
                {
                    File.WriteAllText(generatedPath, file.Contents);
                    filesMapping.add(file.SourcePath, generatedPath);
                }
            }
            compilation = MacroProcessor.EditTrees(
                compilation, sourceMap, results.OfType<ModifiedFile>().Select(f => (f.From, f.To))
            );
            File.WriteAllLines(
                Path.Combine(GENERATED_FOLDER, $"Generated-files-{assemblyName}.txt"),
                filesMapping.dict
                    .SelectMany(kv => kv.Value.Where(path => path.EndsWith(".cs", StringComparison.Ordinal)))
                    .Select(path => path.Replace("/", "\\")));
            return (compilation, diagnostic);
        }

        static Location attrLocation(AttributeData attr) => attr.ApplicationSyntaxReference.GetSyntax().GetLocation();

        static void SetNamedArguments(AttributeData attributeData, Attribute instance) {
            foreach (var arg in attributeData.NamedArguments)
            {
                // if some arguments are invelid they do not appear in NamedArguments list
                // because of that we do not check for errors
                var prop = caseType.GetProperty(arg.Key);
                prop.SetValue(instance, arg.Value.Value);
            }
        }

        static A CreateAttributeByReflection<A>(AttributeData attributeData) where A : Attribute{
            var type = typeof(A);
            var arguments = attributeData.ConstructorArguments;
            var ctor = type.GetConstructors().First(ci => ci.GetParameters().Length == arguments.Length);
            var res = (A) ctor.Invoke(arguments.Select(a => a.Value).ToArray());
            SetNamedArguments(attributeData, res);
            return res;
        }

        static string GenerateAccessor(IFieldSymbol fieldSymbol) {
            var name = fieldSymbol.Name;
            var newName = name.TrimStart('_');
            if (name == newName) newName += "_";
            return $"public {fieldSymbol.Type} {newName} => {name};";
        }

        private static MemberDeclarationSyntax GenerateMatcher(
            SemanticModel model, TypeDeclarationSyntax tds, ImmutableArray<TypeDeclarationSyntax> typesInFile)
        {
            // TODO: ban extendig this class in different files
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
                       $"throw new ArgumentOutOfRangeException(\"obj\", obj, \"Should never reach this\");" +
                       $"}}";
            }

            return CreateStatic(tds, ParseClassMembers(VoidMatch() + Match()));
        }

        private static MemberDeclarationSyntax GenerateCaseClass(RecordAttribute attr, SemanticModel model, TypeDeclarationSyntax cds) {
            var properties = cds.Members.OfType<PropertyDeclarationSyntax>()
                .Where(prop => prop.Modifiers.HasNot(SyntaxKind.StaticKeyword))
                .Where(prop => prop.AccessorList?.Accessors.Any(ads =>
                    ads.IsKind(SyntaxKind.GetAccessorDeclaration)
                    && ads.Body == null
                    && ads.ExpressionBody == null
                ) ?? false)
                .Select(prop => (type: prop.Type, prop.Identifier));

            var fields = cds.Members.OfType<FieldDeclarationSyntax>()
                .Where(field => {
                    var modifiers = field.Modifiers;
                    return modifiers.HasNot(SyntaxKind.StaticKeyword) && modifiers.HasNot(SyntaxKind.ConstKeyword);
                })
                .SelectMany(field => {
                    var decl = field.Declaration;
                    var type = decl.Type;
                    return decl.Variables.Select(varDecl => (type, varDecl.Identifier));
                });

            var fieldsAndProps = fields.Concat(properties).ToArray();

            var constructor = createIf(attr.GenerateConstructor, () =>
                ImmutableList.Create((MemberDeclarationSyntax) SyntaxFactory.ConstructorDeclaration(cds.Identifier)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithParameterList(SyntaxFactory.ParameterList(
                    SyntaxFactory.SeparatedList(fieldsAndProps.Select(f =>
                        SyntaxFactory.Parameter(f.Identifier).WithType(f.type)))))
                .WithBody(SyntaxFactory.Block(fieldsAndProps.Select(f => SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.ThisExpression(),
                        SyntaxFactory.IdentifierName(f.Identifier)), SyntaxFactory.IdentifierName(f.Identifier)))))))
            );
            var paramsStr = Join(", ", fieldsAndProps.Select(f => f.Identifier.ValueText).Select(n => n + ": \" + " + n + " + \""));

            IEnumerable<MemberDeclarationSyntax> createIf(bool condition, Func<IEnumerable<MemberDeclarationSyntax>> a) =>
                condition ? a() : Enumerable.Empty<MemberDeclarationSyntax>();

            var toString = createIf(
                attr.GenerateToString,
                () => ParseClassMembers($"public override string ToString() => \"{cds.Identifier.ValueText}(\" + \"{paramsStr})\";")
            );

            /*
            public override int GetHashCode() {
                unchecked {
                    var hashCode = int1;
                    hashCode = (hashCode * 397) ^ int2;
                    hashCode = (hashCode * 397) ^ (str1 != null ? str1.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (str2 != null ? str2.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (int) uint1;
                    hashCode = (hashCode * 397) ^ structWithHash.GetHashCode();
                    hashCode = (hashCode * 397) ^ structNoHash.GetHashCode();
                    hashCode = (hashCode * 397) ^ float1.GetHashCode();
                    hashCode = (hashCode * 397) ^ double1.GetHashCode();
                    hashCode = (hashCode * 397) ^ long1.GetHashCode();
                    hashCode = (hashCode * 397) ^ bool1.GetHashCode();
                    return hashCode;
                }
            }
            */

            var getHashCode = createIf(attr.GenerateGetHashCode, () => {
                var hashLines = Join("\n", fieldsAndProps.Select(f => {
                    var type = model.GetTypeInfo(f.type).Type;
                    var isValueType = type.IsValueType;
                    var name = f.Identifier.ValueText;
                    string ValueTypeHash(SpecialType sType)
                    {
                        switch (sType)
                        {
                            case SpecialType.System_Byte:
                            case SpecialType.System_SByte:
                            case SpecialType.System_Int16:
                            case SpecialType.System_Int32: return name;
                            case SpecialType.System_UInt32:
                            //TODO: `long` type enums should not cast
                            case SpecialType.System_Enum: return "(int) " + name;
                            default: return name + ".GetHashCode()";
                        }
                    }

                    var fieldHashCode = isValueType
                        ? ValueTypeHash(type.SpecialType)
                        : $"({name} == null ? 0 : {name}.GetHashCode())";
                    return $"hashCode = (hashCode * 397) ^ {(fieldHashCode)}; // {type.SpecialType}";
                }));
                return ParseClassMembers(
                $@"public override int GetHashCode() {{
                    unchecked {{
                        var hashCode = 0;
                        {hashLines}
                        return hashCode;
                    }}
                }}");
            });


            /*
            // class
            private bool Equals(ClassTest other) {
                return int1 == other.int1
                    && int2 == other.int2
                    && string.Equals(str1, other.str1)
                    && string.Equals(str2, other.str2)
                    && uint1 == other.uint1
                    && structWithHash.Equals(other.structWithHash)
                    && structNoHash.Equals(other.structNoHash)
                    && float1.Equals(other.float1)
                    && double1.Equals(other.double1)
                    && long1 == other.long1
                    && bool1 == other.bool1
                    && char1 == other.char1
                    && byte1 == other.byte1
                    && sbyte1 == other.sbyte1
                    && short1 == other.short1
                    && enum1 == other.enum1
                    && byteEnum == other.byteEnum
                    && longEnum == other.longEnum;
            }

            public override bool Equals(object obj) {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is ClassTest && Equals((ClassTest) obj);
            }

            // struct
            public bool Equals(StructTest other) {
                return int1 == other.int1
                    && int2 == other.int2
                    && string.Equals(str1, other.str1)
                    && string.Equals(str2, other.str2)
                    && Equals(classRef, other.classRef);
            }

            public override bool Equals(object obj) {
                if (ReferenceEquals(null, obj)) return false;
                return obj is StructTest && Equals((StructTest) obj);
            }

            TODO: generic fields
            EqualityComparer<B>.Default.GetHashCode(valClass);
            EqualityComparer<B>.Default.Equals(valClass, other.valClass);
            */

            var typeName = cds.Identifier.ValueText + cds.TypeParameterList;

            var equals = createIf(attr.GenerateComparer, () => {
                var isStruct = cds.Kind() == SyntaxKind.StructDeclaration;
                var comparisons = fieldsAndProps.Select(f =>
                {
                    var type = model.GetTypeInfo(f.type).Type;
                    var name = f.Identifier.ValueText;
                    var otherName = "other." + name;
                    switch (type.SpecialType)
                    {
                        case SpecialType.System_Byte:
                        case SpecialType.System_SByte:
                        case SpecialType.System_Int16:
                        case SpecialType.System_UInt16:
                        case SpecialType.System_Int32:
                        case SpecialType.System_UInt32:
                        case SpecialType.System_Int64:
                        case SpecialType.System_UInt64:
                        case SpecialType.System_Enum: return $"{name} == {otherName}";
                        case SpecialType.System_String: return $"string.Equals({name}, {otherName})";
                        default: return $"{name}.Equals({otherName})";
                    }
                });
                var equalsExpr = isStruct ? "left.Equals(right)" : "Equals(left, right)";
                return ParseClassMembers(
                    $"public bool Equals({typeName} other) => {Join(" && ", comparisons)};" +
                    $"public override bool Equals(object obj) {{" +
                    $"  if (ReferenceEquals(null, obj)) return false;" +
                    (!isStruct ? "if (ReferenceEquals(this, obj)) return true;" : "") +
                    $"  return obj is {typeName} && Equals(({typeName}) obj);" +
                    $"}}" +
                    $"public static bool operator ==({typeName} left, {typeName} right) => {equalsExpr};" +
                    $"public static bool operator !=({typeName} left, {typeName} right) => !{equalsExpr};");
            });

            var baseList = attr.GenerateComparer
                // : IEquatable<TypeName>
                ? SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName($"System.IEquatable<{typeName}>"))))
                : Extensions.EmptyBaseList;
            var newMembers = constructor.Concat(toString).Concat(getHashCode).Concat(equals);

            return CreatePartial(cds, newMembers, baseList);
        }

        private static TypeDeclarationSyntax CreatePartial(TypeDeclarationSyntax originalType, IEnumerable<MemberDeclarationSyntax> newMembers, BaseListSyntax baseList)
            => CreateType(
                originalType.Kind(),
                originalType.Identifier,
                originalType.Modifiers.Add(SyntaxKind.PartialKeyword),
                originalType.TypeParameterList,
                SyntaxFactory.List(newMembers),
                baseList);

        private static TypeDeclarationSyntax CreateStatic(TypeDeclarationSyntax originalType, IEnumerable<MemberDeclarationSyntax> newMembers)
            => SyntaxFactory.ClassDeclaration(originalType.Identifier + "Matcher")
                .WithModifiers(SyntaxFactory
                    .TokenList(originalType.Modifiers.Where(k => kindsForExtensionClass.Contains(k.Kind())))
                    .Add(SyntaxKind.StaticKeyword))
                .WithMembers(SyntaxFactory.List(newMembers));

        public static TypeDeclarationSyntax CreateType(
            SyntaxKind kind, SyntaxToken identifier, SyntaxTokenList modifiers, TypeParameterListSyntax typeParams,
            SyntaxList<MemberDeclarationSyntax> members, BaseListSyntax baseList)
        {
            switch (kind)
            {
                case SyntaxKind.ClassDeclaration:
                    return SyntaxFactory.ClassDeclaration(identifier)
                        .WithModifiers(modifiers)
                        .WithTypeParameterList(typeParams)
                        .WithMembers(members)
                        .WithBaseList(baseList);
                case SyntaxKind.StructDeclaration:
                    return SyntaxFactory.StructDeclaration(identifier)
                        .WithModifiers(modifiers)
                        .WithTypeParameterList(typeParams)
                        .WithMembers(members)
                        .WithBaseList(baseList);
                case SyntaxKind.InterfaceDeclaration:
                    return SyntaxFactory.InterfaceDeclaration(identifier)
                        .WithModifiers(modifiers)
                        .WithTypeParameterList(typeParams)
                        .WithMembers(members)
                        .WithBaseList(baseList);
                default:
                    throw new ArgumentOutOfRangeException(kind.ToString());
            }
        }

        // stolen from CodeGeneration.Roslyn
        static MemberDeclarationSyntax AddAncestors(MemberDeclarationSyntax memberNode, MemberDeclarationSyntax generatedType, bool onlyNamespace)
        {
            // Figure out ancestry for the generated type, including nesting types and namespaces.
            foreach (var ancestor in memberNode.Ancestors())
            {
                switch (ancestor)
                {
                    case NamespaceDeclarationSyntax a:
                        generatedType = SyntaxFactory.NamespaceDeclaration(a.Name)
                            .WithUsings(cleanUsings(a.Usings))
                            .WithMembers(SyntaxFactory.SingletonList(generatedType));
                        break;
                    case ClassDeclarationSyntax a:
                        if (onlyNamespace) break;
                        generatedType = a
                            .WithMembers(SyntaxFactory.SingletonList(generatedType))
                            .WithModifiers(a.Modifiers.Add(SyntaxKind.PartialKeyword))
                            .WithoutTrivia()
                            .WithCloseBraceToken(a.CloseBraceToken.WithoutTrivia())
                            .WithBaseList(Extensions.EmptyBaseList)
                            .WithAttributeLists(Extensions.EmptyAttributeList);
                        break;
                    case StructDeclarationSyntax a:
                        if (onlyNamespace) break;
                        generatedType = a
                            .WithMembers(SyntaxFactory.SingletonList(generatedType))
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
            SyntaxFactory.List(usings.Select(u =>
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
            return (BlockSyntax) SyntaxFactory.ParseStatement("{" + syntax + "}");
        }

        public static string Quote(string s) => $"\"{s}\"";
    }
}
