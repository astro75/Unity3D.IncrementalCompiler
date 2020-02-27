using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteLastOrDefault
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
            => p.Rewrite.RewriteAsLoop(
                p.ReturnType,
                new[] {p.Code.CreateLocalVariableDeclaration("_last", SyntaxFactory.DefaultExpression(p.ReturnType))},
                new[] {SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("_last"))},
                p.Collection,
                p.Code.MaybeAddFilter(p.Chain, p.AggregationMethod == Constants.LastOrDefaultWithConditionMethod),
                (inv, arguments, param)
                    => SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName("_last"), SyntaxFactory.IdentifierName(param.Identifier.ValueText))));
    }
}