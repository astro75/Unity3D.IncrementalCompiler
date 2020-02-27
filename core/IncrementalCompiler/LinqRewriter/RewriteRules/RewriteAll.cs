using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteAll
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
            => p.Rewrite.RewriteAsLoop(
                p.Code.CreatePrimitiveType(SyntaxKind.BoolKeyword),
                Enumerable.Empty<StatementSyntax>(),
                new[]
                {
                    SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))
                },
                p.Collection,
                p.Chain,
                (inv, arguments, param) =>
                {
                    var lambda = (LambdaExpressionSyntax) inv.Arguments.First();
                    return SyntaxFactory.IfStatement(
                        SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression,
                            SyntaxFactory.ParenthesizedExpression(
                                p.Code.InlineOrCreateMethod(new Lambda(lambda),
                                    p.Code.CreatePrimitiveType(SyntaxKind.BoolKeyword), param))),
                        SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)));
                });
    }
}