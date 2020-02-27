using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteAny
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
        {
            return p.Rewrite.RewriteAsLoop(
                p.Code.CreatePrimitiveType(SyntaxKind.BoolKeyword),
                Enumerable.Empty<StatementSyntax>(),
                new[]
                {
                    SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression))
                },
                p.Collection,
                p.Code.MaybeAddFilter(p.Chain, p.AggregationMethod == Constants.AnyWithConditionMethod),
                (inv, arguments, param)
                    => SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));
        }
    }
}