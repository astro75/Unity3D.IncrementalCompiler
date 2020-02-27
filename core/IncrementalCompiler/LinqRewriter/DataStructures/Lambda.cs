using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Shaman.Roslyn.LinqRewrite.DataStructures
{
    public class Lambda
    {
        public CSharpSyntaxNode Body { get; }
        public IReadOnlyList<ParameterSyntax> Parameters { get; }
        public AnonymousFunctionExpressionSyntax Syntax { get; }

        public Lambda(AnonymousFunctionExpressionSyntax lambda)
        {
            Body = lambda.Body;
            Syntax = lambda;
            switch (lambda)
            {
                case ParenthesizedLambdaExpressionSyntax syntax:
                    Parameters = syntax.ParameterList.Parameters;
                    break;
                case AnonymousMethodExpressionSyntax expressionSyntax:
                    Parameters = expressionSyntax.ParameterList.Parameters;
                    break;
                case SimpleLambdaExpressionSyntax lambdaExpressionSyntax:
                    Parameters = new[] { lambdaExpressionSyntax.Parameter };
                    break;
            }
        }

        public Lambda(CSharpSyntaxNode statement, ParameterSyntax[] parameters)
        {
            Body = statement;
            Parameters = parameters;
        }
    }
}
