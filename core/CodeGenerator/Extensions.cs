using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IncrementalCompiler {
  public static class Extensions {
    public static readonly BaseListSyntax? EmptyBaseList = null;
    public static SyntaxList<AttributeListSyntax> EmptyAttributeList = SF.List<AttributeListSyntax>();
    public static SyntaxTriviaList EmptyTriviaList = SF.TriviaList();

    public static bool Has(this BasePropertyDeclarationSyntax decl, SyntaxKind kind) => decl.Modifiers.Has(kind);

    public static bool Has(this BaseMethodDeclarationSyntax decl, SyntaxKind kind) => decl.Modifiers.Has(kind);

    public static bool Has(this SyntaxTokenList tokens, SyntaxKind kind) => tokens.Any(m => m.IsKind(kind));

    public static bool HasNot(this BasePropertyDeclarationSyntax decl, SyntaxKind kind) => decl.Modifiers.HasNot(kind);

    public static bool HasNot(this BaseMethodDeclarationSyntax decl, SyntaxKind kind) => decl.Modifiers.HasNot(kind);

    public static bool HasNot(this SyntaxTokenList tokens, SyntaxKind kind) => tokens.All(m => !m.IsKind(kind));

    public static PropertyDeclarationSyntax Remove(this PropertyDeclarationSyntax decl, SyntaxKind kind) =>
      decl.WithModifiers(decl.Modifiers.Remove(kind));

    public static MethodDeclarationSyntax Remove(this MethodDeclarationSyntax decl, SyntaxKind kind) =>
      decl.WithModifiers(decl.Modifiers.Remove(kind));

    public static SyntaxTokenList Remove(this SyntaxTokenList tokens, SyntaxKind kind) => tokens.Remove(SF.Token(kind));

    public static SyntaxTokenList RemoveOfKind(this SyntaxTokenList tokens, SyntaxKind kind) {
      var i = tokens.IndexOfKind(kind);
      return i != -1 ? tokens.RemoveAt(i) : tokens;
    }

    public static int IndexOfKind(this SyntaxTokenList tokens, SyntaxKind kind) {
      var i = 0;
      foreach (var t in tokens)
        if (t.IsKind(kind)) return i;
        else i++;

      return -1;
    }

    public static SyntaxTokenList Add(this SyntaxTokenList modifiers, SyntaxKind kind) {
      return modifiers.Any(m => m.IsKind(kind)) ? modifiers : modifiers.Add(SF.Token(kind));
    }

    public static SyntaxTokenList Modifiers(this MemberDeclarationSyntax member) {
      switch (member)
      {
        case FieldDeclarationSyntax s1:
          return s1.Modifiers;
        case PropertyDeclarationSyntax s2:
          return s2.Modifiers;
        case MethodDeclarationSyntax s:
          return s.Modifiers;
      }

      return SF.TokenList();
    }

    public static MemberDeclarationSyntax
      WithModifiers(this MemberDeclarationSyntax member, SyntaxTokenList modifiers) {
      switch (member)
      {
        case FieldDeclarationSyntax s1:
          return s1.WithModifiers(modifiers);
        case PropertyDeclarationSyntax s2:
          return s2.WithModifiers(modifiers);
        case MethodDeclarationSyntax s:
          return s.WithModifiers(modifiers);
      }

      return member;
    }

    public static LiteralExpressionSyntax StringLiteral(this string value) {
      return SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
    }

    public static B Tap<A, B>(this A a, Func<A, B> func) {
      return func(a);
    }

    public static void VoidTap<A>(this A a, Action<A> act) {
      act(a);
    }

    public static void ForEach<A>(this IEnumerable<A> enumerable, Action<A> act) {
      foreach (var e in enumerable) act(e);
    }

    public static bool IsEnum(this ITypeSymbol t, out SpecialType underlyingType) {
      if (t is INamedTypeSymbol nt && nt.EnumUnderlyingType != null)
      {
        underlyingType = nt.EnumUnderlyingType.SpecialType;
        return true;
      }

      underlyingType = t.SpecialType;
      return false;
    }

    public static string FirstLetterToUpper(this string str) {
      return char.ToUpper(str[0]) + str.Substring(1);
    }

    // https://github.com/nventive/Uno.Roslyn/blob/master/src/Uno.RoslynHelpers/Content/cs/any/Microsoft/CodeAnalysis/SymbolExtensions.cs
    public static bool IsNullable(this ITypeSymbol type) {
      return ((type as INamedTypeSymbol)?.IsGenericType ?? false)
             && type.OriginalDefinition.ToDisplayString()
               .Equals("System.Nullable<T>", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<INamedTypeSymbol> GetAllTypes(this Compilation compilation) {
      return GetAllTypes(compilation.GlobalNamespace);
    }

    static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol @namespace) {
      foreach (var type in @namespace.GetTypeMembers())
      foreach (var nestedType in GetNestedTypes(type))
        yield return nestedType;

      foreach (var nestedNamespace in @namespace.GetNamespaceMembers())
      foreach (var type in GetAllTypes(nestedNamespace))
        yield return type;
    }

    static IEnumerable<INamedTypeSymbol> GetNestedTypes(INamedTypeSymbol type) {
      yield return type;
      foreach (var nestedType in type.GetTypeMembers().SelectMany(GetNestedTypes))
        yield return nestedType;
    }

    public static IAssemblySymbol[] GetReferencedAssembliesAndSelf(this IAssemblySymbol root) {
      var tested = new HashSet<IAssemblySymbol>();
      var notTested = new HashSet<IAssemblySymbol> {root};
      while (notTested.Count > 0)
      {
        var toTest = notTested.ToArray();
        notTested.Clear();
        foreach (var a in toTest) tested.Add(a);

        foreach (var a in toTest)
        foreach (var module in a.Modules)
        foreach (var assembly in module.ReferencedAssemblySymbols)
          if (!tested.Contains(assembly))
            notTested.Add(assembly);
      }

      return tested.OrderBy(_ => _.Name).ToArray();
    }

    public static string EnsureDoesNotEndWith(this string s, string suffix) {
      return s.EndsWith(suffix, StringComparison.Ordinal) ? s.Substring(0, s.Length - suffix.Length) : s;
    }

    public static string EnsureDoesNotStartWith(this string s, string prefix) {
      return s.StartsWith(prefix, StringComparison.Ordinal) ? s.Substring(prefix.Length) : s;
    }
  }
}
