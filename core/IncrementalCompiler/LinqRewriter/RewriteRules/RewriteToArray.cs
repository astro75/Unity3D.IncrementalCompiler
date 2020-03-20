using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteToArray
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
        {
            var count = p.Chain.All(x => Constants.MethodsThatPreserveCount.Contains(x.MethodName))
                ? p.Code.CreateCollectionCount(p.Collection, false, p.Data.uniqueCounter) : null;

            if (count != null)
            {
                var arrayIdentifier = SyntaxFactory.IdentifierName("_array" + p.Data.uniqueCounter);
                return p.Rewrite.RewriteAsLoop(
                    p.ReturnType,
                    new[]
                    {
                        p.Code.CreateLocalVariableDeclaration("_array" + p.Data.uniqueCounter,
                            SyntaxFactory.ArrayCreationExpression(SyntaxFactory.ArrayType(((ArrayTypeSyntax) p.ReturnType).ElementType,
                                SyntaxFactory.List(new[]  {SyntaxFactory.ArrayRankSpecifier(p.Code.CreateSeparatedList(count))}))))
                    },
                    new[] {SyntaxFactory.ReturnStatement(arrayIdentifier)},
                    p.Collection,
                    p.Chain,
                    (inv, arguments, param)
                        => p.Code.CreateStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.ElementAccessExpression(arrayIdentifier, SyntaxFactory.BracketedArgumentList( p.Code.CreateSeparatedList( SyntaxFactory.Argument(SyntaxFactory.IdentifierName("_index"))))),
                            SyntaxFactory.IdentifierName(param.Identifier.ValueText))));
            }

            var listIdentifier = SyntaxFactory.IdentifierName("_list" + p.Data.uniqueCounter);
            var listType = SyntaxFactory.ParseTypeName($"System.Collections.Generic.List<{((ArrayTypeSyntax) p.ReturnType).ElementType}>");
            return p.Rewrite.RewriteAsLoop(
                p.ReturnType,
                new[]
                {
                    p.Code.CreateLocalVariableDeclaration("_list" + p.Data.uniqueCounter, SyntaxFactory.ObjectCreationExpression(listType, p.Code.CreateArguments(Enumerable.Empty<ArgumentSyntax>()), null))
                },
                new[]
                {
                    SyntaxFactory.ReturnStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, listIdentifier, SyntaxFactory.IdentifierName("ToArray"))))
                },
                p.Collection,
                p.Chain,
                (inv, arguments, param)
                    => p.Code.CreateStatement(SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, listIdentifier, SyntaxFactory.IdentifierName("Add")),
                        p.Code.CreateArguments(SyntaxFactory.IdentifierName(param.Identifier.ValueText)))));
        }
    }
}
