using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteToList
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
        {
            var count = p.Chain.All(x => Constants.MethodsThatPreserveCount.Contains(x.MethodName))
                ? p.Code.CreateCollectionCount(p.Collection, true) : null;
            
            var listIdentifier = SyntaxFactory.IdentifierName("_list");
            return p.Rewrite.RewriteAsLoop(
                p.ReturnType,
                new[]
                {
                    p.Code.CreateLocalVariableDeclaration("_list",
                        SyntaxFactory.ObjectCreationExpression(
                            SyntaxFactory.ParseTypeName( $"System.Collections.Generic.List<{p.Info.GetItemType(p.SemanticReturnType).ToDisplayString()}>"),
                            p.Code.CreateArguments(count != null ? new[] {count} : Enumerable.Empty<ExpressionSyntax>()),
                            null))
                },
                p.AggregationMethod == Constants.ReverseMethod
                    ? new StatementSyntax[]
                    {
                        SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName("_list"), SyntaxFactory.IdentifierName("Reverse")))),
                        SyntaxFactory.ReturnStatement(listIdentifier)
                    }
                    : new[] {SyntaxFactory.ReturnStatement(listIdentifier)},
                p.Collection,
                p.Chain,
                (inv, arguments, param) => p.Code.CreateStatement(SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, listIdentifier,
                        SyntaxFactory.IdentifierName("Add")), p.Code.CreateArguments(SyntaxFactory.IdentifierName(param.Identifier.ValueText)))));
        }
    }
}