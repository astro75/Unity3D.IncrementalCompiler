using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IncrementalCompiler {
  public static partial class CodeGeneration {
    static IEnumerable<MemberDeclarationSyntax> GenerateLambdaInterface(
      TypeDeclarationSyntax interfaceDeclarationSyntax
    ) {
      var interfaceTypeParameters = interfaceDeclarationSyntax.TypeParameterList;
      
      var methods = interfaceDeclarationSyntax.Members.SelectMany(member => member switch {
        MethodDeclarationSyntax m => new[] {m},
        _ => Enumerable.Empty<MethodDeclarationSyntax>()
      }).ToArray();

      ImmutableHashSet<string> usedRecordNames = methods.Select(m => m.Identifier.Text).ToImmutableHashSet();
      string deriveMethodName(string fromName) => deriveName(fromName, ref usedRecordNames, fromLeft: false);
      string deriveDelegateName(string fromName) => deriveName(fromName, ref usedRecordNames, fromLeft: true);

      var recordNameWithoutGenerics = IdentifierName($"Lambda{interfaceDeclarationSyntax.Identifier.Text}");
      NameSyntax 
        recordName =
          interfaceTypeParameters == null
            ? recordNameWithoutGenerics
            : GenericName(recordNameWithoutGenerics.Identifier, toTypeArgumentList(interfaceTypeParameters)),
        interfaceName =
          interfaceTypeParameters == null
            ? IdentifierName(interfaceDeclarationSyntax.Identifier)
            : GenericName(interfaceDeclarationSyntax.Identifier, toTypeArgumentList(interfaceTypeParameters));

      var unsupportedMethods = 
        methods.Where(method => method.TypeParameterList != null)
        .Select(method => method.ToFullString()).ToArray();
      if (unsupportedMethods.Length != 0) throw new Exception(
        "Methods with generic parameters are not supported because they do not make sense!\n\n" +
        "They do not make sense because:\n" +
        "* To store a delegate in the record as a field it has to have all of its type parameters to be \n" +
        "  declared on the record.\n" +
        "* This type parameter is declared on the method and is not known until the method is called.\n" +
        "* Thus it is impossible to store such methods as delegates.\n\n" +
        $"Unsupported methods:\n{string.Join("\n", unsupportedMethods)}"
      );
      
      var delegates = methods.Select(method => {
        // We need to make sure that the type parameters from the interface are copied but at the same time we need
        // to make sure that type parameters from interface and method declarations do not clash.
        var delegate_ =
          DelegateDeclaration(method.ReturnType, identifier: deriveDelegateName(method.Identifier.Text))
            .WithParameterList(method.ParameterList)
            // copy public/internal modifiers
            .WithModifiers(interfaceDeclarationSyntax.Modifiers);
        // Parameter which will be used in a record constructor.
        var parameter =
          Parameter(
            // copy the name of the method but make sure it doesn't clash with existing names/method names
            Identifier(deriveMethodName(method.Identifier.Text))
          ).WithType(QualifiedName(
            recordName,
            IdentifierName(delegate_.Identifier)
            // TODO: handle generics
            // GenericName(delegate_.Identifier)
          ));
        // Method implementation that goes into the record
        var recordMethod =
          MethodDeclaration(method.ReturnType, method.Identifier)
            // interface members need to be public
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(method.ParameterList)
            .WithExpressionBody(ArrowExpressionClause(
              InvocationExpression(IdentifierName(parameter.Identifier))
                .WithArgumentList(ArgumentList(SeparatedList(
                  method.ParameterList.Parameters.Select(parameter =>
                    Argument(IdentifierName(parameter.Identifier))
                  )
                )))
            ))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
        return (method, delegate_, parameter, recordMethod);
      }).ToArray();

      var recordDeclaration =
        RecordDeclaration(Token(SyntaxKind.RecordKeyword), recordNameWithoutGenerics.Identifier)
          // Copy public/internal modifiers
          .WithModifiers(interfaceDeclarationSyntax.Modifiers)
          // Add the generics from the interface
          .WithTypeParameterList(interfaceDeclarationSyntax.TypeParameterList)
          .WithConstraintClauses(interfaceDeclarationSyntax.ConstraintClauses)
          // Implement the interface
          .WithBaseList(BaseList(SingletonSeparatedList<BaseTypeSyntax>(SimpleBaseType(interfaceName))))
          // Create the record parameters
          .WithParameterList(ParameterList(SeparatedList(
            delegates.Select(_ => _.parameter),
            delegates.Skip(1).Select(_ => Token(SyntaxKind.CommaToken))
          )))
          .WithOpenBraceToken(Token(SyntaxKind.OpenBraceToken))
          .WithMembers(new SyntaxList<MemberDeclarationSyntax>(delegates.SelectMany(tpl => 
            new MemberDeclarationSyntax[] { tpl.delegate_, tpl.recordMethod }
          )))
          .WithCloseBraceToken(Token(SyntaxKind.CloseBraceToken));

      yield return recordDeclaration;
      // Console.Out.WriteLine(recordDeclaration.NormalizeWhitespace().ToFullString());
      // Environment.Exit(0);
      
      static string deriveName(string fromName, ref ImmutableHashSet<string> usedNames, bool fromLeft) {
        var derived = fromName;
        while (usedNames.Contains(derived)) derived = fromLeft ? $"_{derived}" : $"{derived}_";
        usedNames = usedNames.Add(derived);
        return derived;
      }
    }
  }
}