using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Shaman.Roslyn.LinqRewrite.DataStructures
{
    public class Lambda
    {
        public CSharpSyntaxNode Body { get; }
        public IReadOnlyList<ParameterSyntax> Parameters { get; }

        public ExpressionSyntax Syntax;

        public static Lambda Create(ExpressionSyntax syntax) {
            return syntax switch {
                AnonymousFunctionExpressionSyntax s => new Lambda(s),
                { } e => new Lambda(e)
            };
        }

        public Lambda(AnonymousFunctionExpressionSyntax lambda)
        {
            Body = lambda.Body;
            Parameters = lambda switch {
                ParenthesizedLambdaExpressionSyntax syntax => syntax.ParameterList.Parameters,
                AnonymousMethodExpressionSyntax expressionSyntax => expressionSyntax.ParameterList.Parameters,
                SimpleLambdaExpressionSyntax lambdaExpressionSyntax => new[] {lambdaExpressionSyntax.Parameter},
                _ => Parameters
            };
        }

        public Lambda(ExpressionSyntax syntax) {
            Syntax = syntax;
        }

        public Lambda(CSharpSyntaxNode statement, ParameterSyntax[] parameters)
        {
            Body = statement;
            Parameters = parameters;
        }
    }
}
