using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.Services
{
    public class RewriteDataService
    {
        private static RewriteDataService _instance;
        public static RewriteDataService Instance => _instance ??= new RewriteDataService();
        
        public SemanticModel Semantic;
        public int LastId;
        
        public IEnumerable<VariableCapture> CurrentFlow;
        public TypeDeclarationSyntax CurrentType;
        public string CurrentMethodName;
        public bool CurrentMethodIsStatic;
        public TypeParameterListSyntax CurrentMethodTypeParameters;
        public SyntaxList<TypeParameterConstraintClauseSyntax> CurrentMethodConstraintClauses;
        
        public readonly List<Tuple<TypeDeclarationSyntax, MethodDeclarationSyntax>> MethodsToAddToCurrentType =
            new List<Tuple<TypeDeclarationSyntax, MethodDeclarationSyntax>>();
        
        internal delegate StatementSyntax AggregationDelegate(LinqStep invocation, ArgumentListSyntax arguments, ParameterSyntax param);
        internal AggregationDelegate CurrentAggregation;
    }
}