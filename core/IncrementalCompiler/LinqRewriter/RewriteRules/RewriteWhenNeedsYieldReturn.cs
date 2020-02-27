using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteWhenNeedsYieldReturn
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
            => p.Rewrite.RewriteAsLoop(
                p.ReturnType,
                Enumerable.Empty<StatementSyntax>(),
                Enumerable.Empty<StatementSyntax>(),
                p.Collection,
                p.Chain,
                (inv, arguments, param)
                    => SyntaxFactory.YieldStatement(SyntaxKind.YieldReturnStatement,
                        SyntaxFactory.IdentifierName(param.Identifier.ValueText)),
                true
            );
    }
}