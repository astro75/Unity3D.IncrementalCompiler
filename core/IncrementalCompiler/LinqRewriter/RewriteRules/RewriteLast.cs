using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteLast
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
            => p.Rewrite.RewriteAsLoop(
                p.ReturnType,
                new[]
                {
                    p.Code.CreateLocalVariableDeclaration("_last", SyntaxFactory.DefaultExpression(p.ReturnType)),
                    p.Code.CreateLocalVariableDeclaration("_found", SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression))
                },
                new StatementSyntax[]
                {
                    SyntaxFactory.IfStatement(
                        SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression,
                            SyntaxFactory.IdentifierName("_found")),
                        p.Code.CreateThrowException("System.InvalidOperationException", "The sequence did not contain any elements.")),
                    SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("_last"))
                },
                p.Collection,
                p.Code.MaybeAddFilter(p.Chain, p.AggregationMethod == Constants.LastWithConditionMethod),
                (inv, arguments, param) => SyntaxFactory.Block(
                    SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_found"), SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))),
                    SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_last"), SyntaxFactory.IdentifierName(param.Identifier.ValueText)))));
    }
}