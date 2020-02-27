using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Shaman.Roslyn.LinqRewrite.DataStructures
{
    public class LinqStep
    {
        public Lambda Lambda { get; set; }

        public LinqStep(string methodName, ExpressionSyntax[] arguments, InvocationExpressionSyntax invocation = null)
        {
            MethodName = methodName;
            Arguments = arguments;
            Invocation = invocation;
        }
        
        public string MethodName { get; }
        public ExpressionSyntax[] Arguments { get; }
        public InvocationExpressionSyntax Invocation { get; }
        public override string ToString() => MethodName;
    }
}
