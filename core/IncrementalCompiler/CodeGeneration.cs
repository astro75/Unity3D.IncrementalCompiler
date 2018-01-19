using System;
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

namespace IncrementalCompiler
{
    public static class CodeGeneration
    {
        public const string GENERATED_FOLDER = "Generated";

        public static CSharpCompilation Run(CSharpCompilation compilation, ImmutableArray<SyntaxTree> trees, CSharpParseOptions parseOptions, string assemblyName)
        {
            Directory.CreateDirectory(GENERATED_FOLDER);
            var oldCompilation = compilation;
            var newTrees = trees.AsParallel().SelectMany(tree =>
            {
                var model = oldCompilation.GetSemanticModel(tree);
                var root = tree.GetCompilationUnitRoot();
                var newMembers = ImmutableList<MemberDeclarationSyntax>.Empty;
                var typesInFile = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
                foreach (var tds in typesInFile)
                {
                    var attrs = model.GetDeclaredSymbol(tds).GetAttributes();
                    foreach (var attr in attrs)
                    {
                        var attrClassName = attr.AttributeClass.ToDisplayString();
                        if (attrClassName == typeof(CaseAttribute).FullName)
                        {
                            newMembers = newMembers.Add(AddAncestors(tds, GenerateCaseClass(model, tds)));
                        }
                        if (attrClassName == typeof(MatcherAttribute).FullName)
                        {
                            newMembers = newMembers.Add(
                                AddAncestors(tds,GenerateMatcher(model, (ClassDeclarationSyntax) tds, typesInFile))
                            );
                        }
                    }
                }
                if (newMembers.Any())
                {
                    var nt = CSharpSyntaxTree.Create(
                        SyntaxFactory.CompilationUnit()
                            .WithUsings(root.Usings)
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
            return compilation;
        }

        private static MemberDeclarationSyntax GenerateMatcher(
            SemanticModel model, ClassDeclarationSyntax cds, ImmutableArray<TypeDeclarationSyntax> typesInFile)
        {
            // TODO: ban extendig this class in different files
            // TODO: generics ?

            var baseTypeSymbol = model.GetDeclaredSymbol(cds);

            var childTypes = typesInFile.Where(t => model.GetDeclaredSymbol(t).BaseType?.Equals(baseTypeSymbol) ?? false);

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

            var childNames = childTypes.Select(t => t.Identifier.ValueText + t.TypeParameterList).ToArray();

            string VoidMatch()
            {
                var parameters = Join(", ", childNames.Select((name, idx) => $"Action<{name}> a{idx}"));
                var body = Join("\n", childNames.Select((name, idx) =>
                  $"var val{idx} = this as {name};" +
                  $"if (val{idx} != null) {{ a{idx}(val{idx}); return; }}"));

                return $"public void voidMatch({parameters}) {{{body}}}";
            }

            string Match()
            {
                var parameters = Join(", ", childNames.Select((name, idx) => $"Func<{name}, A> f{idx}"));
                var body = Join("\n", childNames.Select((name, idx) =>
                    $"var val{idx} = this as {name};" +
                    $"if (val{idx} != null) return f{idx}(val{idx});"));

                return $"public A match<A>({parameters}) {{" +
                       $"{body}" +
                       $"throw new ArgumentOutOfRangeException(\"this\", this, \"Should never reach this\");" +
                       $"}}";
            }

            return CreatePartial(cds, ParseClassMembers(VoidMatch() + Match()), null);
        }

        private static MemberDeclarationSyntax GenerateCaseClass(SemanticModel model, TypeDeclarationSyntax cds)
        {
            var fields = cds.Members.OfType<FieldDeclarationSyntax>().SelectMany(field =>
            {
                var decl = field.Declaration;
                var type = decl.Type;
                return decl.Variables.Select(varDecl => (type, varDecl.Identifier));
            }).ToArray();
            var constructor = SyntaxFactory.ConstructorDeclaration(cds.Identifier)
                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                .WithParameterList(SyntaxFactory.ParameterList(
                    SyntaxFactory.SeparatedList(fields.Select(f =>
                        SyntaxFactory.Parameter(f.Identifier).WithType(f.type)))))
                .WithBody(SyntaxFactory.Block(fields.Select(f => SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.ThisExpression(),
                        SyntaxFactory.IdentifierName(f.Identifier)), SyntaxFactory.IdentifierName(f.Identifier))))));
            var paramsStr = Join(", ", fields.Select(f => f.Identifier.ValueText).Select(n => n + ": \" + " + n + " + \""));
            var toString = ParseClassMembers(
                $"public override string ToString() => \"{cds.Identifier.ValueText}(\" + \"{paramsStr})\";");

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

            var hashLines = Join("\n", fields.Select(f =>
            {
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
                return "hashCode = (hashCode * 397) ^ " + (isValueType ? ValueTypeHash(type.SpecialType) : $"({name} == null ? 0 : {name}.GetHashCode())") + ";";
            }));
            var getHashCode = ParseClassMembers(
            $@"public override int GetHashCode() {{
                unchecked {{
                    var hashCode = 0;
                    {hashLines}
                    return hashCode;
                }}
            }}");

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

            var isStruct = cds.Kind() == SyntaxKind.StructDeclaration;

            var typeName = cds.Identifier.ValueText + cds.TypeParameterList;
            var comparisons = fields.Select(f =>
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
            var equals = ParseClassMembers(
                $"public bool Equals({typeName} other) => {Join(" && ", comparisons)};" +
                $"public override bool Equals(object obj) {{" +
                $"  if (ReferenceEquals(null, obj)) return false;" +
                (!isStruct ? "if (ReferenceEquals(this, obj)) return true;" : "") +
                $"  return obj is {typeName} && Equals(({typeName}) obj);" +
                $"}}" +
                $"public static bool operator ==({typeName} left, {typeName} right) => {equalsExpr};" +
                $"public static bool operator !=({typeName} left, {typeName} right) => !{equalsExpr};");

            // : IEquatable<TypeName>
            var baseList = SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName($"System.IEquatable<{typeName}>"))));
            var newMembers = new[] { constructor }.Concat(toString).Concat(getHashCode).Concat(equals);

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
        public static MemberDeclarationSyntax AddAncestors(MemberDeclarationSyntax memberNode, MemberDeclarationSyntax generatedType)
        {
            // Figure out ancestry for the generated type, including nesting types and namespaces.
            foreach (var ancestor in memberNode.Ancestors())
            {
                switch (ancestor)
                {
                    case NamespaceDeclarationSyntax a:
                        generatedType = a
                            .WithMembers(SyntaxFactory.SingletonList(generatedType))
                            .WithoutTrivia();
                        break;
                    case ClassDeclarationSyntax a:
                        generatedType = a
                            .WithMembers(SyntaxFactory.SingletonList(generatedType))
                            .WithModifiers(a.Modifiers.Add(SyntaxKind.PartialKeyword))
                            .WithoutTrivia()
                            .WithCloseBraceToken(a.CloseBraceToken.WithoutTrivia())
                            .WithBaseList(Extensions.EmptyBaseList);
                        break;
                    case StructDeclarationSyntax a:
                        generatedType = a
                            .WithMembers(SyntaxFactory.SingletonList(generatedType))
                            .WithModifiers(a.Modifiers.Add(SyntaxKind.PartialKeyword))
                            .WithoutTrivia()
                            .WithCloseBraceToken(a.CloseBraceToken.WithoutTrivia())
                            .WithBaseList(Extensions.EmptyBaseList);
                        break;
                }
            }
            return generatedType;
        }

        public static SyntaxList<MemberDeclarationSyntax> ParseClassMembers(string syntax)
        {
            var cls = (ClassDeclarationSyntax)CSharpSyntaxTree.ParseText($"class C {{ {syntax} }}").GetCompilationUnitRoot().Members[0];
            return cls.Members;
        }

        public static string Quote(string s) => $"\"{s}\"";
    }
}
