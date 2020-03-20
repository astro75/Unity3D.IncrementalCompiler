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
                    var newBlock = p.Code.InlineOrCreateMethod(
                        new Lambda(lambda), p.Code.CreatePrimitiveType(SyntaxKind.BoolKeyword), param, isVoid: false
                    );

                    var statement = SyntaxFactory.IfStatement(
                        SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression,
                            SyntaxFactory.ParenthesizedExpression(newBlock.Item2)),
                        SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)));

                    return SyntaxFactory.Block(newBlock.Item1.Concat(new[] {statement}));
                });
    }
}
