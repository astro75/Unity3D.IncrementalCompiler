using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteCount
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
                new[] {SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("_count"))},
                p.Collection,
                p.Code.MaybeAddFilter(p.Chain, p.AggregationMethod == Constants.CountWithConditionMethod || p.AggregationMethod == Constants.LongCountWithConditionMethod),
                (inv, arguments, param)
                    => SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, SyntaxFactory.IdentifierName("_count"))));
    }
}