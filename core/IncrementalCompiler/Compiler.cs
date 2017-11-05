using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GenerationAttributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mono.CompilerServices.SymbolWriter;
using NLog;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static System.String;

namespace IncrementalCompiler
{
    public class Compiler
    {
        private const string GENERATED = "Generated";
        private Logger _logger = LogManager.GetLogger("Compiler");
        private CSharpCompilation _compilation;
        private CompileOptions _options;
        private FileTimeList _referenceFileList;
        private FileTimeList _sourceFileList;
        private Dictionary<string, MetadataReference> _referenceMap;
        private Dictionary<string, SyntaxTree> _sourceMap;
        private MemoryStream _outputDllStream;
        private MemoryStream _outputDebugSymbolStream;

        public CompileResult Build(CompileOptions options)
        {
            if (_compilation == null ||
                _options.WorkDirectory != options.WorkDirectory ||
                _options.AssemblyName != options.AssemblyName ||
                _options.Output != options.Output ||
                _options.NoWarnings.SequenceEqual(options.NoWarnings) == false ||
                _options.Defines.SequenceEqual(options.Defines) == false)
            {
                return BuildFull(options);
            }
            else
            {
                return BuildIncremental(options);
            }
        }

        private CompileResult BuildFull(CompileOptions options)
        {
            var result = new CompileResult();

            _logger.Info("BuildFull");
            _options = options;

            _referenceFileList = new FileTimeList();
            _referenceFileList.Update(options.References);

            _sourceFileList = new FileTimeList();
            _sourceFileList.Update(options.Files);

            _referenceMap = options.References.ToDictionary(
               file => file,
               file => CreateReference(file));

            var parseOption = new CSharpParseOptions(LanguageVersion.CSharp6, DocumentationMode.Parse, SourceCodeKind.Regular, options.Defines);
            _sourceMap = options.Files.ToDictionary(
                file => file,
                file => ParseSource(file, parseOption));

            var specificDiagnosticOptions = options.NoWarnings.ToDictionary(x => x, _ => ReportDiagnostic.Suppress);
            _compilation = CSharpCompilation.Create(
                options.AssemblyName,
                _sourceMap.Values,
                _referenceMap.Values,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                    .WithSpecificDiagnosticOptions(specificDiagnosticOptions)
                    .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default)
                    .WithAllowUnsafe(options.Options.Contains("-unsafe")));

            ModifyCompilation(parseOption, Path.GetFileNameWithoutExtension(options.AssemblyName));

            Emit(result);

            return result;
        }

        private void ModifyCompilation(CSharpParseOptions parseOption, string assemblyName)
        {
            var currentDir = new Uri(Directory.GetCurrentDirectory());
            var newTrees = new List<SyntaxTree>();
            foreach (var tree in _compilation.SyntaxTrees)
            {
                var model = _compilation.GetSemanticModel(tree);
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
                            newMembers = newMembers.Add(AddAncestors(tds, GenerateMatcher(model, (ClassDeclarationSyntax)tds, typesInFile)));
                        }
                    }
                }
                if (newMembers.Any())
                {
                    var nt = CSharpSyntaxTree.Create(
                        SF.CompilationUnit()
                            .WithUsings(root.Usings)
                            .WithMembers(SF.List(newMembers))
                            .NormalizeWhitespace(),
                        path: Path.Combine(GENERATED, currentDir.MakeRelativeUri(
                            new Uri(tree.FilePath)).ToString().Replace('/', Path.DirectorySeparatorChar)),
                        options: parseOption);
                    newTrees.Add(nt);
                }
            }
            _compilation = _compilation.AddSyntaxTrees(newTrees);
            foreach (var syntaxTree in newTrees)
            {
                var path = syntaxTree.FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, syntaxTree.GetText().ToString());
            }
            File.WriteAllLines(Path.Combine(GENERATED, $"Generated-files-{assemblyName}.txt"), newTrees.Select(tree => tree.FilePath));
        }

        private MemberDeclarationSyntax GenerateMatcher(SemanticModel model, ClassDeclarationSyntax cds, ImmutableArray<TypeDeclarationSyntax> typesInFile)
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

        private MemberDeclarationSyntax GenerateCaseClass(SemanticModel model, TypeDeclarationSyntax cds)
        {
            var fields = cds.Members.OfType<FieldDeclarationSyntax>().SelectMany(field =>
            {
                var decl = field.Declaration;
                var type = decl.Type;
                return decl.Variables.Select(varDecl => (type, varDecl.Identifier));
            }).ToArray();
            var constructor = SF.ConstructorDeclaration(cds.Identifier)
                .WithModifiers(SF.TokenList(SF.Token(SyntaxKind.PublicKeyword)))
                .WithParameterList(SF.ParameterList(
                    SF.SeparatedList(fields.Select(f =>
                        SF.Parameter(f.Identifier).WithType(f.type)))))
                .WithBody(SF.Block(fields.Select(f => SF.ExpressionStatement(SF.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SF.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SF.ThisExpression(),
                        SF.IdentifierName(f.Identifier)), SF.IdentifierName(f.Identifier))))));
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
            var baseList = SF.BaseList(SF.SingletonSeparatedList<BaseTypeSyntax>(SF.SimpleBaseType(SF.ParseTypeName($"System.IEquatable<{typeName}>"))));
            var newMembers = new[] { constructor }.Concat(toString).Concat(getHashCode).Concat(equals);

            return CreatePartial(cds, newMembers, baseList);
        }

        private TypeDeclarationSyntax CreatePartial(TypeDeclarationSyntax originalType, IEnumerable<MemberDeclarationSyntax> newMembers, BaseListSyntax baseList)
            => CreateType(
                originalType.Kind(),
                originalType.Identifier,
                originalType.Modifiers.Add(SyntaxKind.PartialKeyword),
                originalType.TypeParameterList,
                SF.List(newMembers),
                baseList);

        public TypeDeclarationSyntax CreateType(
            SyntaxKind kind, SyntaxToken identifier, SyntaxTokenList modifiers, TypeParameterListSyntax typeParams,
            SyntaxList<MemberDeclarationSyntax> members, BaseListSyntax baseList)
        {
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
                            .WithMembers(SF.SingletonList(generatedType))
                            .WithLeadingTrivia(SF.TriviaList())
                            .WithTrailingTrivia(SF.TriviaList());
                        break;
                    case ClassDeclarationSyntax a:
                        generatedType = a
                            .WithMembers(SF.SingletonList(generatedType))
                            .WithLeadingTrivia(SF.TriviaList())
                            .WithTrailingTrivia(SF.TriviaList());
                        break;
                    case StructDeclarationSyntax a:
                        generatedType = a
                            .WithMembers(SF.SingletonList(generatedType))
                            .WithLeadingTrivia(SF.TriviaList())
                            .WithTrailingTrivia(SF.TriviaList());
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

        private CompileResult BuildIncremental(CompileOptions options)
        {
            var result = new CompileResult();

            _logger.Info("BuildIncremental");
            _options = options;

            // update reference files

            var referenceChanges = _referenceFileList.Update(options.References);
            foreach (var file in referenceChanges.Added)
            {
                _logger.Info("+ {0}", file);
                var reference = CreateReference(file);
                _compilation = _compilation.AddReferences(reference);
                _referenceMap.Add(file, reference);
            }
            foreach (var file in referenceChanges.Changed)
            {
                _logger.Info("* {0}", file);
                var reference = CreateReference(file);
                _compilation = _compilation.RemoveReferences(_referenceMap[file])
                                           .AddReferences(reference);
                _referenceMap[file] = reference;
            }
            foreach (var file in referenceChanges.Removed)
            {
                _logger.Info("- {0}", file);
                _compilation = _compilation.RemoveReferences(_referenceMap[file]);
                _referenceMap.Remove(file);
            }

            // update source files

            var sourceChanges = _sourceFileList.Update(options.Files);
            var parseOption = new CSharpParseOptions(LanguageVersion.CSharp6, DocumentationMode.Parse, SourceCodeKind.Regular, options.Defines);
            foreach (var file in sourceChanges.Added)
            {
                _logger.Info("+ {0}", file);
                var syntaxTree = ParseSource(file, parseOption);
                _compilation = _compilation.AddSyntaxTrees(syntaxTree);
                _sourceMap.Add(file, syntaxTree);
            }
            foreach (var file in sourceChanges.Changed)
            {
                _logger.Info("* {0}", file);
                var syntaxTree = ParseSource(file, parseOption);
                _compilation = _compilation.RemoveSyntaxTrees(_sourceMap[file])
                                           .AddSyntaxTrees(syntaxTree);
                _sourceMap[file] = syntaxTree;
            }
            foreach (var file in sourceChanges.Removed)
            {
                _logger.Info("- {0}", file);
                _compilation = _compilation.RemoveSyntaxTrees(_sourceMap[file]);
                _sourceMap.Remove(file);
            }

            // emit or reuse prebuilt output

            var reusePrebuilt = _outputDllStream != null && (
                (_options.PrebuiltOutputReuse == PrebuiltOutputReuseType.WhenNoChange &&
                 sourceChanges.Empty && referenceChanges.Empty) ||
                (_options.PrebuiltOutputReuse == PrebuiltOutputReuseType.WhenNoSourceChange &&
                 sourceChanges.Empty && referenceChanges.Added.Count == 0 && referenceChanges.Removed.Count == 0));

            if (reusePrebuilt)
            {
                _logger.Info("Reuse prebuilt output");

                // write dll

                var dllFile = Path.Combine(_options.WorkDirectory, _options.Output);
                using (var dllStream = new FileStream(dllFile, FileMode.Create))
                {
                    _outputDllStream.Seek(0L, SeekOrigin.Begin);
                    _outputDllStream.CopyTo(dllStream);
                }

                // write pdb or mdb

                switch (_options.DebugSymbolFile)
                {
                    case DebugSymbolFileType.Pdb:
                        var pdbFile = Path.Combine(_options.WorkDirectory, Path.ChangeExtension(_options.Output, ".pdb"));
                        using (var debugSymbolStream = new FileStream(pdbFile, FileMode.Create))
                        {
                            _outputDebugSymbolStream.Seek(0L, SeekOrigin.Begin);
                            _outputDebugSymbolStream.CopyTo(debugSymbolStream);
                        }
                        break;

                    case DebugSymbolFileType.PdbToMdb:
                    case DebugSymbolFileType.Mdb:
                        var mdbFile = Path.Combine(_options.WorkDirectory, _options.Output + ".mdb");
                        using (var debugSymbolStream = new FileStream(mdbFile, FileMode.Create))
                        {
                            _outputDebugSymbolStream.Seek(0L, SeekOrigin.Begin);
                            _outputDebugSymbolStream.CopyTo(debugSymbolStream);
                        }
                        break;
                }

                result.Succeeded = true;
            }
            else
            {
                _logger.Info("Emit");

                Emit(result);
            }

            return result;
        }

        private MetadataReference CreateReference(string file)
        {
            return MetadataReference.CreateFromFile(Path.Combine(_options.WorkDirectory, file));
        }

        private SyntaxTree ParseSource(string file, CSharpParseOptions parseOption)
        {
            var fileFullPath = Path.Combine(_options.WorkDirectory, file);
            var text = File.ReadAllText(fileFullPath);
            return CSharpSyntaxTree.ParseText(text, parseOption, fileFullPath, Encoding.UTF8);
        }

        private void Emit(CompileResult result)
        {
            _outputDllStream = new MemoryStream();
            _outputDebugSymbolStream = _options.DebugSymbolFile != DebugSymbolFileType.None ? new MemoryStream() : null;

            // emit to memory

            var r = _options.DebugSymbolFile == DebugSymbolFileType.Mdb
                ? _compilation.EmitWithMdb(_outputDllStream, _outputDebugSymbolStream)
                : _compilation.Emit(_outputDllStream, _outputDebugSymbolStream);

            // memory to file

            var dllFile = Path.Combine(_options.WorkDirectory, _options.Output);
            var mdbFile = Path.Combine(_options.WorkDirectory, _options.Output + ".mdb");
            var pdbFile = Path.Combine(_options.WorkDirectory, Path.ChangeExtension(_options.Output, ".pdb"));

            var emitDebugSymbolFile = _options.DebugSymbolFile == DebugSymbolFileType.Mdb ? mdbFile : pdbFile;

            using (var dllStream = new FileStream(dllFile, FileMode.Create))
            {
                _outputDllStream.Seek(0L, SeekOrigin.Begin);
                _outputDllStream.CopyTo(dllStream);
            }

            if (_outputDebugSymbolStream != null)
            {
                using (var debugSymbolStream = new FileStream(emitDebugSymbolFile, FileMode.Create))
                {
                    _outputDebugSymbolStream.Seek(0L, SeekOrigin.Begin);
                    _outputDebugSymbolStream.CopyTo(debugSymbolStream);
                }
            }

            // gather result

            foreach (var d in r.Diagnostics)
            {
                if (d.Severity == DiagnosticSeverity.Warning && d.IsWarningAsError == false)
                    result.Warnings.Add(GetDiagnosticString(d, "warning"));
                else if (d.Severity == DiagnosticSeverity.Error || d.IsWarningAsError)
                    result.Errors.Add(GetDiagnosticString(d, "error"));
            }

            result.Succeeded = r.Success;

            // pdb to mdb when required

            if (_options.DebugSymbolFile == DebugSymbolFileType.PdbToMdb)
            {
                var code = ConvertPdb2Mdb(dllFile);
                _logger.Info("pdb2mdb exited with {0}", code);
                File.Delete(pdbFile);

                // read converted mdb file to cache contents
                _outputDebugSymbolStream = new MemoryStream(File.ReadAllBytes(mdbFile));
            }
        }

        private string GetDiagnosticString(Diagnostic diagnostic, string type)
        {
            var line = diagnostic.Location.GetLineSpan();

            // Path could be null
            if (string.IsNullOrEmpty(line.Path))
                return $"None: " + $"{type} {diagnostic.Id}: {diagnostic.GetMessage()}";

            // Unity3d must have a relative path starting with "Assets/".
            var path = (line.Path.StartsWith(_options.WorkDirectory + "/") || line.Path.StartsWith(_options.WorkDirectory + "\\"))
                ? line.Path.Substring(_options.WorkDirectory.Length + 1)
                : line.Path;

            return $"{path}({line.StartLinePosition.Line + 1},{line.StartLinePosition.Character + 1}): " + $"{type} {diagnostic.Id}: {diagnostic.GetMessage()}";
        }

        public static int ConvertPdb2Mdb(string dllFile)
        {
            var toolPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "pdb2mdb.exe");
            var process = new Process();
            process.StartInfo = new ProcessStartInfo(toolPath, '"' + dllFile + '"');
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.Start();
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
