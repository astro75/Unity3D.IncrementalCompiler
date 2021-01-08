using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IncrementalCompiler {
  public static partial class CodeGeneration {
    static TypeArgumentListSyntax toTypeArgumentList(TypeParameterListSyntax typeParameters) =>
      TypeArgumentList(SeparatedList<TypeSyntax>(
        typeParameters.Parameters.Select(p => IdentifierName(p.Identifier))
      ));
  }
}