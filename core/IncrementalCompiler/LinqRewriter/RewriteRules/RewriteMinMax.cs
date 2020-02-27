using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;
using SyntaxExtensions = Shaman.Roslyn.LinqRewrite.Extensions.SyntaxExtensions;

namespace Shaman.Roslyn.LinqRewrite.RewriteRules
{
    public static class RewriteMinMax
    {
        public static ExpressionSyntax Rewrite(RewriteParameters p)
        {
            var minmax = p.AggregationMethod.Contains(".Max") ? "max_" : "min_";
            var elementType = (p.ReturnType as NullableTypeSyntax)?.ElementType ?? p.ReturnType;
            return p.Rewrite.RewriteAsLoop(
                p.ReturnType,
                new[]
                {
                    p.Code.CreateLocalVariableDeclaration("found_", SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression)),
                    p.Code.CreateLocalVariableDeclaration(minmax,
                        SyntaxFactory.CastExpression(elementType,
                            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))))
                },
                new[]
                {
                    SyntaxFactory.Block(
                        SyntaxFactory.IfStatement(
                            SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression,
                                SyntaxFactory.IdentifierName("found_")),
                            p.ReturnType == elementType
                                ? (StatementSyntax) p.Code.CreateThrowException("System.InvalidOperationException", "The sequence did not contain any elements.")
                                : SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression))
                        ),
                        SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(minmax))
                    )
                },
                p.Collection,
                p.Code.MaybeAddSelect(p.Chain, p.Node.ArgumentList.Arguments.Count != 0),
                (inv, arguments, param) =>
                {
                    var identifierNameSyntax = SyntaxFactory.IdentifierName(param.Identifier.ValueText);
                    return SyntaxExtensions.IfNullableIsNotNull(elementType != p.ReturnType, identifierNameSyntax, x =>
                    {
                        var assignmentExpressionSyntax = SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                                SyntaxFactory.IdentifierName(minmax), x);
                        
                        var condition = SyntaxFactory.BinaryExpression(p.AggregationMethod.Contains(".Max") ? SyntaxKind.GreaterThanExpression : SyntaxKind.LessThanExpression,
                            x,
                            SyntaxFactory.IdentifierName(minmax));
                            
                        var kind = ((PredefinedTypeSyntax) elementType).Keyword.Kind();
                        if (kind == SyntaxKind.DoubleKeyword || kind == SyntaxKind.FloatKeyword)
                        {
                            condition = SyntaxFactory.BinaryExpression(SyntaxKind.LogicalOrExpression, 
                                condition,
                                SyntaxFactory.InvocationExpression(
                                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, elementType, SyntaxFactory.IdentifierName("IsNaN")), p.Code.CreateArguments(x)));
                        }

                        return SyntaxFactory.IfStatement(SyntaxFactory.IdentifierName("found_"),
                            SyntaxFactory.Block(SyntaxFactory.IfStatement(condition, SyntaxFactory.ExpressionStatement(assignmentExpressionSyntax))),
                            SyntaxFactory.ElseClause(SyntaxFactory.Block(
                                SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, 
                                    SyntaxFactory.IdentifierName("found_"),
                                    SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression))),
                                SyntaxFactory.ExpressionStatement(assignmentExpressionSyntax))));
                    });
                });
        }
    }
}