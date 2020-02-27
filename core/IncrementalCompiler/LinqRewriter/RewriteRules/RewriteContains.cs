using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteContains
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
        {
            var elementType = SyntaxFactory.ParseTypeName(ModelExtensions
                .GetTypeInfo(p.Data.Semantic, p.Node.ArgumentList.Arguments.First().Expression).ConvertedType
                .ToDisplayString());
            
            var comparerIdentifier = ((elementType as NullableTypeSyntax)?.ElementType ?? elementType) is PredefinedTypeSyntax
                    ? null : SyntaxFactory.IdentifierName("comparer_");
            
            return p.Rewrite.RewriteAsLoop(
                p.Code.CreatePrimitiveType(SyntaxKind.BoolKeyword),
                comparerIdentifier != null
                    ? new StatementSyntax[]
                    {
                        p.Code.CreateLocalVariableDeclaration("comparer_",
                            SyntaxFactory.ParseExpression(
                                $"System.Collections.Generic.EqualityComparer<{elementType}>.Default"))
                    }
                    : Enumerable.Empty<StatementSyntax>(),
                new[]
                {
                    SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression))
                },
                p.Collection,
                p.Chain,
                (inv, arguments, param) =>
                {
                    var target = SyntaxFactory.IdentifierName("_target");
                    var current = SyntaxFactory.IdentifierName(param.Identifier.ValueText);
                    var condition = comparerIdentifier != null
                        ? SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, comparerIdentifier, SyntaxFactory.IdentifierName("Equals")), p.Code.CreateArguments(current, target))
                        : (ExpressionSyntax) SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression, current, target);
                    
                    return SyntaxFactory.IfStatement(condition,
                        SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));
                },
                additionalParameters: new[]
                {
                    Tuple.Create(p.Code.CreateParameter("_target", elementType), p.Node.ArgumentList.Arguments.First().Expression)
                }
            );
        }
    }
}