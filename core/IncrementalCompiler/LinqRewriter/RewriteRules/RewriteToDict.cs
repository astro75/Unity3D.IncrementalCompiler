using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteToDict
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
        {
            var dictIdentifier = SyntaxFactory.IdentifierName("_dict");
            return p.Rewrite.RewriteAsLoop(
                p.ReturnType,
                new[]
                {
                    p.Code.CreateLocalVariableDeclaration("_dict",
                        SyntaxFactory.ObjectCreationExpression(p.ReturnType,
                            p.Code.CreateArguments(Enumerable.Empty<ArgumentSyntax>()), null))
                },
                new[] {SyntaxFactory.ReturnStatement(dictIdentifier)},
                p.Collection,
                p.Chain,
                (inv, arguments, param) =>
                {
                    var keyLambda = (AnonymousFunctionExpressionSyntax) p.Node.ArgumentList.Arguments.First().Expression;
                    var valueLambda =  (AnonymousFunctionExpressionSyntax) p.Node.ArgumentList.Arguments.ElementAtOrDefault(1) ?.Expression;
                    return p.Code.CreateStatement(SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, dictIdentifier,
                            SyntaxFactory.IdentifierName("Add")), p.Code.CreateArguments(p.Code.InlineOrCreateMethod(new Lambda(keyLambda),
                                SyntaxFactory.ParseTypeName(p.Info.GetLambdaReturnType(keyLambda).ToDisplayString()), param),
                            p.AggregationMethod == Constants.ToDictionaryWithKeyValueMethod
                                ? p.Code.InlineOrCreateMethod(new Lambda(valueLambda),
                                    SyntaxFactory.ParseTypeName(p.Info.GetLambdaReturnType(valueLambda) .ToDisplayString()), param)
                                : SyntaxFactory.IdentifierName(param.Identifier.ValueText))));
                }
            );
        }
    }
}