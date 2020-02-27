using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Shaman.Roslyn.LinqRewrite.Extensions
{
    public static class SyntaxExtensions
    {
        public static MethodDeclarationSyntax WithStatic(this MethodDeclarationSyntax syntax, bool isStatic)
        {
            return isStatic 
                ? syntax.AddModifiers(SyntaxFactory.Token(SyntaxKind.StaticKeyword)) 
                : syntax.WithModifiers(SyntaxFactory.TokenList());
        }
        public static ParameterSyntax WithRef(this ParameterSyntax syntax, bool isRef)
        {
            return isRef 
                ? syntax.AddModifiers(SyntaxFactory.Token(SyntaxKind.RefKeyword)) 
                : syntax.WithModifiers(SyntaxFactory.TokenList());
        }
        public static ArgumentSyntax WithRef(this ArgumentSyntax syntax, bool isRef)
        {
            return syntax.WithRefOrOutKeyword(isRef 
                ? SyntaxFactory.Token(SyntaxKind.RefKeyword) 
                : default);
        }
        public static StatementSyntax IfNullableIsNotNull(bool nullable, IdentifierNameSyntax currentValue, Func<ExpressionSyntax, StatementSyntax> p)
        {
            var k = nullable ? (ExpressionSyntax)SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, currentValue, SyntaxFactory.IdentifierName("GetValueOrDefault"))) : currentValue;
            return nullable ? SyntaxFactory.IfStatement(SyntaxFactory.BinaryExpression(SyntaxKind.NotEqualsExpression, currentValue, SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)), p(k)) : p(k);
        }

        public static bool IsAnonymousType(ITypeSymbol t) 
            => t.ToDisplayString().Contains("anonymous type:");
    }
}
