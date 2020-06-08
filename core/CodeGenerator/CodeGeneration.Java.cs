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
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IncrementalCompiler {
  public static partial class CodeGeneration {
    class GeneratedJavaFile : GeneratedFile {
      public readonly JavaFile JavaFile;

      public GeneratedJavaFile(string sourcePath, Location location, JavaFile javaFile) : base(sourcePath, location) {
        JavaFile = javaFile;
      }
    }

    public partial class GeneratedFilesMapping {
      public Dictionary<string, List<JavaFile>> javaFilesDict = new Dictionary<string, List<JavaFile>>();

      int javaVersion = 1, lastUsedJavaVersion;
      SyntaxTree? prevousTree;

      public bool tryAddJavaFile(string key, JavaFile value) {
        if (enumerate(javaFilesDict).Any(jf => jf.Module == value.Module && jf.Path == value.Path)) return false;

        javaVersion++;
        addValue(javaFilesDict, key, value);
        return true;
      }

      public CSharpCompilation updateCompilation(CSharpCompilation compilation, CSharpParseOptions options,
        string assemblyName, string generatedFilesDir) {
        if (javaFilesDict.Count == 0) return compilation;
        if (lastUsedJavaVersion != javaVersion) {
          lastUsedJavaVersion = javaVersion;

          var newTree = generateJavaTree(options, assemblyName, generatedFilesDir);
          var path = newTree.FilePath;
          {
            if (File.Exists(path)) File.Delete(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, newTree.GetText().ToString());
          }
          // this code smells a little
          filesDict["GENERATED_JAVA"] = new List<string>(new[] {path});
          var result =
            prevousTree == null
              ? compilation.AddSyntaxTrees(newTree)
              : compilation.ReplaceSyntaxTree(prevousTree, newTree);
          prevousTree = newTree;
          return result;
        }

        return compilation;
      }

      SyntaxTree generateJavaTree(CSharpParseOptions options, string assemblyName, string generatedFilesDir) {
        var className = assemblyName.Replace("-", "").Replace(".", "");
        var data = enumerate(javaFilesDict).Select(jf =>
          $"new JavaFile(module: \"{jf.Module}\", path: {asVerbatimString(jf.Path)}, contents: {asVerbatimString(jf.Contents)})"
        );
        var ns = "com.tinylabproductions.generated.java";
        var tree = CSharpSyntaxTree.ParseText(
          "#if UNITY_EDITOR\n" +
          "using GenerationAttributes;\n" +
          $"namespace {ns} {{\n" +
          $"public static class {className} {{\n" +
          "public static readonly JavaFile[] javaFiles = {" +
          Join(", ", data) +
          "}; }}\n" +
          "#endif",
          options,
          Path.Combine(generatedFilesDir, ns.Replace('.', Path.DirectorySeparatorChar), className + ".cs")
        );
        return CSharpSyntaxTree.Create(
          tree.GetCompilationUnitRoot().NormalizeWhitespace(),
          options,
          tree.FilePath,
          Encoding.UTF8
        );
      }

      void maybeRemoveJavaFile(string filePath) {
        if (javaFilesDict.ContainsKey(filePath)) {
          javaFilesDict.Remove(filePath);
          javaVersion++;
        }
      }
    }

    // TODO: clean this class
    // refactor parts to tlplib
    class JavaClassFile {
      readonly IMethodSymbol[] allMethods;
      public readonly Location Location;
      readonly List<string> Methods = new List<string>();
      public readonly string Module, Imports, ClassBody;
      readonly INamedTypeSymbol Symbol;

      public JavaClassFile(INamedTypeSymbol symbol, string module, string imports, string classBody,
        Location location) {
        Symbol = symbol;
        Module = module;
        Imports = imports;
        ClassBody = classBody;
        allMethods = AllInterfaceMembers(symbol).OfType<IMethodSymbol>().ToArray();
        Location = location;
      }

      string Package => "com.generated." + Module;
      public string PackageWithClass => Package + "." + Symbol.Name;

      static ImmutableArray<ISymbol> AllInterfaceMembers(INamedTypeSymbol symbol) {
        return symbol.GetMembers().AddRange(symbol.AllInterfaces.SelectMany(i => i.GetMembers()));
      }

      public void AddMethod(string methodBody, IMethodSymbol methodSymbol) {
        var isConstructor = methodSymbol.MethodKind == MethodKind.Constructor;
        var modifier = methodSymbol.IsStatic ? "static " : "";
        var parameters = Join(", ",
          methodSymbol.Parameters.Select(p => $"final {ToJavaType(p.Type)} {p.Name}").ToArray());
        var typeAndName = isConstructor
          ? Symbol.Name
          : $"{ToJavaType(methodSymbol.ReturnType)} {methodSymbol.Name}";
        Methods.Add(
          $"public {modifier}{typeAndName}({parameters}) {{\n" +
          $"{methodBody}\n" +
          "}\n"
        );
      }

      public string GenerateJava() {
        return $"package {Package};\n\n" +
               Imports +
               $"public class {Symbol.Name} {{\n" +
               $"{ClassBody}\n" +
               Join("\n", Methods) +
               "}";
      }

      public IEnumerable<MemberDeclarationSyntax> GenerateMembers() {
        var line = "static UnityEngine.AndroidJavaClass jc = UnityEngine.Application.isEditor ? null : " +
                   $"new UnityEngine.AndroidJavaClass(\"{PackageWithClass}\");";
        var secondLine = Symbol.IsStatic ? "" : "readonly UnityEngine.AndroidJavaObject jo;";
        return ParseClassMembers(line + "\n" + secondLine);
      }

      static bool isSubType(ITypeSymbol? type, string baseType) {
        if (type == null) return false;
        if (type.ToDisplayString() == baseType)
          return true;
        return isSubType(type.BaseType, baseType);
      }

      public BaseMethodDeclarationSyntax GenerateMethod(IMethodSymbol symbol, BaseMethodDeclarationSyntax syntax) {
        var isConstructor = symbol.MethodKind == MethodKind.Constructor;
        var isVoid = symbol.ReturnType.SpecialType == SpecialType.System_Void;
        var returnStatement = isVoid ? "" : "return ";
        var callStetement = isConstructor ? "jo = new UnityEngine.AndroidJavaObject" :
          symbol.IsStatic ? "jc.CallStatic" : "jo.Call";
        var firstParam = $"\"{(isConstructor ? PackageWithClass : symbol.Name)}\"";

        string parameterName(IParameterSymbol ps) {
          var type = ps.Type;
          var name = ps.Name;
          if (isSubType(type, "com.tinylabproductions.TLPLib.Android.Bindings.Binding"))
            return name + ".java";
          return name;
        }

        var remainingParams = symbol.Parameters.Select(parameterName);
        var arguments = Join(", ", new[] {firstParam}.Concat(remainingParams));

        switch (syntax) {
          case MethodDeclarationSyntax mds:
            var genericReturn = isVoid ? "" : "<" + mds.ReturnType + ">";
            return mds
              .WithBody(ParseBlock($"{returnStatement}{callStetement}{genericReturn}({arguments});"))
              .WithExpressionBody(null)
              .WithSemicolonToken(SF.Token(SyntaxKind.None));
          case ConstructorDeclarationSyntax cds:
            return cds
              .WithBody(ParseBlock($"{returnStatement}{callStetement}({arguments});"))
              .WithExpressionBody(null)
              .WithSemicolonToken(SF.Token(SyntaxKind.None));
        }

        throw new Exception("Wrong syntax type: " + syntax.GetType());
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
            var baseType = type.BaseType;
            if (baseType == null) break;
            var arguments = baseType.TypeArguments;
            if (arguments.Length == 0) break;
            switch (arguments[0].SpecialType) {
              // this code is never reached.
              // TODO: find a way to detect nullable types in C# (int?, bool?, ...)
              case SpecialType.System_Boolean: return "Boolean";
              case SpecialType.System_Byte: return "Byte";
              case SpecialType.System_Char: return "Character";
              case SpecialType.System_Int16: return "Short";
              case SpecialType.System_Int32: return "Integer";
              case SpecialType.System_Int64: return "Long";
              case SpecialType.System_Single: return "Float";
              case SpecialType.System_Double: return "Double";
            }

            break;
        }


        foreach (var attrData in type.GetAttributes())
          if (attrData.AttributeClass?.ToDisplayString() == typeof(JavaBindingAttribute).FullName) {
            var instance = CreateAttributeByReflection<JavaBindingAttribute>(attrData);
            return instance.JavaClass;
          }

        if (isSubType(type, "UnityEngine.AndroidJavaProxy") || isSubType(type, "UnityEngine.AndroidJavaObject"))
          return "Object";

        throw new Exception($"Unsupported type: {type.ToDisplayString()}");
      }

      IEnumerable<string> InterfaceMethods() {
        return allMethods.Select(m => {
          var parameters = m.Parameters.Select(p => $"final {ToJavaType(p.Type)} {p.Name}");
          return $"void {m.Name}({Join(", ", parameters)});";
        });
      }

      public string GenerateJavaInterface() {
        return $"package {Package};\n\n" +
               $"public interface {Symbol.Name} " + Block(InterfaceMethods());
      }

      public string JavaFilePath() {
        return PackageWithClass.Replace('.', Path.DirectorySeparatorChar) + ".java";
      }

      public ClassDeclarationSyntax GetInterfaceClass() {
        return ParseClass(
          // JavaBinding attribute does nothing here.
          // Compiler does all code generation in one step,
          // so we can't depend on generated classes when generating other code
          // $"[GenerationAttributes.JavaBinding(\"{PackageWithClass}\")]\n" +
          $"public class {Symbol.Name}Proxy : com.tinylabproductions.TLPLib.Android.JavaListenerProxy" +
          Block(
            Join("\n", allMethods.Select(m => {
              if (m.ReturnType.SpecialType != SpecialType.System_Void) throw new Exception("Return type must be void");
              var parameterTypes = m.Parameters.Select(p => p.Type.ToString()).ToArray();
              var genericArgs = parameterTypes.Length == 0 ? "" : $"<{Join(", ", parameterTypes)}>";
              return $"public event System.Action{genericArgs} {m.Name};";
            })),
            $"public {Symbol.Name}Proxy() : base(\"{PackageWithClass}\"){{}}" +
            "protected override void invokeOnMain(string methodName, object[] args)" + Block(
              "  switch(methodName)" + Block(
                allMethods.Select(m => {
                  var invokeParams = Join(", ", m.Parameters.Select((p, idx) => $"({p.Type}) args[{idx}]"));
                  return $"case \"{m.Name}\": {m.Name}?.Invoke({invokeParams}); return;";
                })
              ),
              "base.invokeOnMain(methodName, args);"
            ),
            "public void registerLogger(string prefix, com.tinylabproductions.TLPLib.Logger.ILog log)" + Block(
              allMethods.Select(m => {
                var paramNames = m.Parameters.Select(p => p.Name).ToArray();
                var paramsStr = paramNames.Length == 0
                  ? "\"\""
                  : Join(" + \", \" + ", paramNames.Select(p => $"{p}.ToString()"));
                return
                  $"{m.Name} += ({Join(", ", paramNames)}) => com.tinylabproductions.TLPLib.Logger.ILogExts.debug(log, prefix + \"{m.Name}(\" + {paramsStr} + \")\");";
              })
            )
          )
        );
      }
    }
  }
}
