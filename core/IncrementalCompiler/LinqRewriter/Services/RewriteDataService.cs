using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.Services
{
    public class RewriteDataService
    {
        // private static RewriteDataService _instance;
        // public static RewriteDataService Instance => _instance ??= new RewriteDataService();

        public SemanticModel Semantic;
        public int LastId;

        public IEnumerable<VariableCapture> CurrentFlow;
        public string CurrentMethodName;

        public TypeParameterListSyntax CurrentMethodTypeParameters;
        public SyntaxList<TypeParameterConstraintClauseSyntax> CurrentMethodConstraintClauses;

        public readonly List<(CSharpSyntaxNode, MethodDeclarationSyntax)> MethodsToAddToCurrentType =
            new List<(CSharpSyntaxNode, MethodDeclarationSyntax)>();

        public bool InMethod = false;
        public readonly HashSet<string> UsedNames = new HashSet<string>();

        internal delegate StatementSyntax AggregationDelegate(LinqStep invocation, ArgumentListSyntax arguments, ParameterSyntax param);
        internal AggregationDelegate CurrentAggregation;

        public bool UseStatic => CurrentTypes.Count != 0 && CurrentTypes.Peek().node is TypeDeclarationSyntax;

        public readonly Stack<(CSharpSyntaxNode node, bool useStatic)> CurrentTypes = new Stack<(CSharpSyntaxNode, bool)>();

        public void AddMethod(MethodDeclarationSyntax method) {
            if (CurrentTypes.Count > 0)
            {
                var type = CurrentTypes.Peek();
                MethodsToAddToCurrentType.Add((type.node, method));
                UsedNames.Add(method.Identifier.ValueText);
            }
        }
    }
}
