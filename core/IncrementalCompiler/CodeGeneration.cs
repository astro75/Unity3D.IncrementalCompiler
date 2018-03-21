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

        public static (CSharpCompilation, ICollection<Diagnostic>) Run(
            CSharpCompilation compilation,
            ImmutableArray<SyntaxTree> trees,
            CSharpParseOptions parseOptions,
            string assemblyName
        ) {
            Directory.CreateDirectory(GENERATED_FOLDER);
            var oldCompilation = compilation;
            var diagnostic = new List<Diagnostic>();

            void tryAttribute(AttributeData attr, Action a) {
                try { a(); }
                catch (Exception e) {
                    diagnostic.Add(Diagnostic.Create(new DiagnosticDescriptor(
                        "ER0001", "Error", $"Compiler error: {e.Message}({e.Source}) at {e.StackTrace}", "Error", DiagnosticSeverity.Error, true
                    ), attr.ApplicationSyntaxReference.GetSyntax().GetLocation()));
                }
            }

            var newTrees = trees.AsParallel().SelectMany(tree =>
            {
                var model = oldCompilation.GetSemanticModel(tree);
                var root = tree.GetCompilationUnitRoot();
                var newMembers = ImmutableList<MemberDeclarationSyntax>.Empty;
                var typesInFile = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
                foreach (var tds in typesInFile)
                {
                    bool treeContains(SyntaxReference syntaxRef) =>
                        tree == syntaxRef.SyntaxTree && tds.Span.Contains(syntaxRef.Span);

                    var symbol = model.GetDeclaredSymbol(tds);
                    foreach (var attr in symbol.GetAttributes())
                    {
                        if (!treeContains(attr.ApplicationSyntaxReference)) continue;
                        var attrClassName = attr.AttributeClass.ToDisplayString();
                        if (attrClassName == caseType.FullName)
                        {
                            tryAttribute(attr, () => {
                                var instance = new RecordAttribute();
                                foreach (var arg in attr.NamedArguments)
                                {
                                    // if some arguments are invelid they do not appear in NamedArguments list
                                    // because of that we do not check for errors
                                    var prop = caseType.GetProperty(arg.Key);
                                    prop.SetValue(instance, arg.Value.Value);
                                }
                                newMembers = newMembers.Add(AddAncestors(tds, GenerateCaseClass(instance, model, tds), onlyNamespace: false));
                            });
                        }
                        if (attrClassName == typeof(MatcherAttribute).FullName)
                        {
                            tryAttribute(attr, () =>
                            {
                                newMembers = newMembers.Add(
                                    AddAncestors(tds, GenerateMatcher(model, tds, typesInFile), onlyNamespace: true)
                                );
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
                                        tryAttribute(attr, () =>
                                        {
                                            newClassMembers = newClassMembers.Add(GenerateAccessor(fieldSymbol));
                                        });
                                    }
                                    // TODO: generic way to add new attributes
                                    if (attrClassName == typeof(ThreadStaticAttribute).FullName)
                                    {
                                        tryAttribute(attr, () => throw new Exception($"Can't use {nameof(ThreadStaticAttribute)} in Unity"));
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
                }
                if (newMembers.Any())
                {
                    var nt = CSharpSyntaxTree.Create(
                        SyntaxFactory.CompilationUnit()
                            .WithUsings(cleanUsings(root.Usings))
                            .WithMembers(SyntaxFactory.List(newMembers))
                            .NormalizeWhitespace(),
                        path: Path.Combine(GENERATED_FOLDER, tree.FilePath),
                        options: parseOptions,
                        encoding: Encoding.UTF8);
                    return new[] {nt};
                }
                return Enumerable.Empty<SyntaxTree>();
            }).ToArray();
            compilation = compilation.AddSyntaxTrees(newTrees);
            foreach (var syntaxTree in newTrees)
            {
                var path = syntaxTree.FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, syntaxTree.GetText().ToString());
            }
            File.WriteAllLines(
                Path.Combine(GENERATED_FOLDER, $"Generated-files-{assemblyName}.txt"),
                compilation.SyntaxTrees
                    .Select(tree => tree.FilePath)
                    .Where(path => path.StartsWith(GENERATED_FOLDER, StringComparison.Ordinal))
                    .Select(path => path.Replace("/", "\\")));
            return (compilation, diagnostic);
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

        static SyntaxList<MemberDeclarationSyntax> ParseClassMembers(string syntax)
        {
            var cls = (ClassDeclarationSyntax)CSharpSyntaxTree.ParseText($"class C {{ {syntax} }}").GetCompilationUnitRoot().Members[0];
            return cls.Members;
        }

        public static string Quote(string s) => $"\"{s}\"";
    }
}
