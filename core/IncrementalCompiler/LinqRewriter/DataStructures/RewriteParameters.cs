using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.Services;

namespace Shaman.Roslyn.LinqRewrite.DataStructures
{
    public class RewriteParameters : IDisposable
    {
        public readonly RewriteService Rewrite;
        public readonly CodeCreationService Code;
        public readonly RewriteDataService Data;
        public readonly SyntaxInformationService Info;
        
        public string AggregationMethod;
        public ExpressionSyntax Collection;
        public TypeSyntax ReturnType;
        public List<LinqStep> Chain;
        public InvocationExpressionSyntax Node;
        public ITypeSymbol SemanticReturnType;
        
        public RewriteParameters()
        {
            Rewrite = RewriteService.Instance;
            Code = CodeCreationService.Instance;
            Data = RewriteDataService.Instance;
            Info = SyntaxInformationService.Instance;
        }

        public void SetData(string aggregationMethod, ExpressionSyntax collection,
            TypeSyntax returnType, ITypeSymbol semanticReturnType, List<LinqStep> chain, InvocationExpressionSyntax node)
        {
            AggregationMethod = aggregationMethod;
            Collection = collection;
            ReturnType = returnType;
            Chain = chain;
            Node = node;
            SemanticReturnType = semanticReturnType;
        }

        public void Dispose() => RewriteParametersHolder.ReturnParameters(this);
    }
}