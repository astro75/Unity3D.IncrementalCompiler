using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteFirst
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
            => p.Rewrite.RewriteAsLoop(
                p.ReturnType,
                Enumerable.Empty<StatementSyntax>(),
                new[]
                {
                    p.Code.CreateThrowException("System.InvalidOperationException", "The sequence did not contain any elements.")
                },
                p.Collection,
                p.Code.MaybeAddFilter(p.Chain, p.AggregationMethod == Constants.FirstWithConditionMethod),
                (inv, arguments, param)
                    => SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(param.Identifier.ValueText)));
    }
}