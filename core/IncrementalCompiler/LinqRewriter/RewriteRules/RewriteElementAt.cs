using System;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteElementAt
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
            => p.Rewrite.RewriteAsLoop(
                p.ReturnType,
                new[]
                {
                    p.Code.CreateLocalVariableDeclaration("_count",
                        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression,
                            SyntaxFactory.ParseToken(p.AggregationMethod == Constants.LongCountMethod ||
                                                     p.AggregationMethod == Constants.LongCountWithConditionMethod
                                ? "0L" : "0")))
                },
                new[]
                {
                    p.AggregationMethod == Constants.ElementAtMethod
                        ? (StatementSyntax) p.Code.CreateThrowException("System.InvalidOperationException", "The specified index is not included in the sequence.")
                        : SyntaxFactory.ReturnStatement(SyntaxFactory.DefaultExpression(p.ReturnType))
                },
                p.Collection,
                p.Code.MaybeAddFilter(p.Chain,
                    p.AggregationMethod == Constants.CountWithConditionMethod ||
                    p.AggregationMethod == Constants.LongCountWithConditionMethod),
                (inv, arguments, param)
                    => SyntaxFactory.IfStatement(
                        SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression,
                            SyntaxFactory.IdentifierName("_requestedPosition"),
                            SyntaxFactory.PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, SyntaxFactory.IdentifierName("_count"))),
                        SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(param.Identifier.ValueText))),
                additionalParameters: new[]
                {
                    Tuple.Create(p.Code.CreateParameter("_requestedPosition", p.Code.CreatePrimitiveType(SyntaxKind.IntKeyword)), p.Node.ArgumentList.Arguments.First().Expression)
                }
            );
    }
}