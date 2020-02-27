using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;
using Shaman.Roslyn.LinqRewrite.Extensions;

namespace Shaman.Roslyn.LinqRewrite.Services
{
    public class RewriteService
    {
        private static RewriteService _instance;
        public static RewriteService Instance => _instance ??= new RewriteService();

        private readonly RewriteDataService _data;
        private readonly SyntaxInformationService _info;
        private readonly CodeCreationService _code;
        private readonly ProcessingStepCreationService _processingStep;
        public Func<SyntaxNode, SyntaxNode> Visit;

        public RewriteService()
        {
            _data = RewriteDataService.Instance;
            _info = SyntaxInformationService.Instance;
            _code = CodeCreationService.Instance;
            _processingStep = ProcessingStepCreationService.Instance;
        }
        
        internal ExpressionSyntax RewriteAsLoop(TypeSyntax returnType, IEnumerable<StatementSyntax> prologue,
            IEnumerable<StatementSyntax> epilogue, ExpressionSyntax collection, List<LinqStep> chain,
            RewriteDataService.AggregationDelegate k, bool noAggregation = false,
            IEnumerable<Tuple<ParameterSyntax, ExpressionSyntax>> additionalParameters = null)
        {
            var old = _data.CurrentAggregation;
            _data.CurrentAggregation = k;

            var collectionType = _data.Semantic.GetTypeInfo(collection).Type;
            var collectionItemType = _info.GetItemType(collectionType);
            if (collectionItemType == null) throw new NotSupportedException();
            var collectionSemanticType = _data.Semantic.GetTypeInfo(collection).Type;

            var parameters = new[] {_code.CreateParameter(Constants.ItemsName, collectionSemanticType)}.Concat(
                _data.CurrentFlow.Select(x => _code.CreateParameter(x.Name, _info.GetSymbolType(x.Symbol)).WithRef(x.Changes)));
            if (additionalParameters != null) parameters = parameters.Concat(additionalParameters.Select(x => x.Item1));

            var functionName = _info.GetUniqueName($"{_data.CurrentMethodName}_ProceduralLinq");
            var arguments = _code.CreateArguments(new[] {SyntaxFactory.Argument(SyntaxFactory.IdentifierName(Constants.ItemName))}.Concat(
                _data.CurrentFlow.Select(x => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(x.Name)).WithRef(x.Changes))));

            var loopContent = _processingStep.CreateProcessingStep(chain, chain.Count - 1,
                SyntaxFactory.ParseTypeName(collectionItemType.ToDisplayString()), Constants.ItemName, arguments, noAggregation);

            var foreachStatement = collectionType.ToDisplayString().StartsWith("System.Collections.Generic.List<") || collectionType is IArrayTypeSymbol
                ? GetForStatement(collection, loopContent)
                : GetForEachStatement(loopContent);

            var coreFunction = GetCoreMethod(returnType, prologue, epilogue, 
                functionName, parameters, collectionSemanticType, foreachStatement);

            _data.MethodsToAddToCurrentType.Add(Tuple.Create(_data.CurrentType, coreFunction));
            var args = new[] {SyntaxFactory.Argument((ExpressionSyntax) Visit(collection))}
                    .Concat(arguments.Arguments.Skip(1));
            
            if (additionalParameters != null) args = args.Concat(additionalParameters.Select(x => SyntaxFactory.Argument(x.Item2)));
            var inv = SyntaxFactory.InvocationExpression(
                _code.CreateMethodNameSyntaxWithCurrentTypeParameters(functionName), _code.CreateArguments(args));

            _data.CurrentAggregation = old;
            return inv;
        }

        private StatementSyntax GetForStatement(ExpressionSyntax collection, StatementSyntax loopContent) 
            => SyntaxFactory.ForStatement(
                SyntaxFactory.VariableDeclaration(
                    _code.CreatePrimitiveType(SyntaxKind.IntKeyword), _code.CreateSeparatedList(SyntaxFactory.VariableDeclarator("_index")
                        .WithInitializer(SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0)))))), 
                default,
                SyntaxFactory.BinaryExpression(SyntaxKind.LessThanExpression,
                    SyntaxFactory.IdentifierName("_index"), 
                    _code.CreateCollectionCount(collection, false)),
                _code.CreateSeparatedList<ExpressionSyntax>(
                    SyntaxFactory.PostfixUnaryExpression(SyntaxKind.PostIncrementExpression,
                        SyntaxFactory.IdentifierName("_index"))),
                SyntaxFactory.Block(new StatementSyntax[]
                {
                    _code.CreateLocalVariableDeclaration(Constants.ItemName,
                        SyntaxFactory.ElementAccessExpression(
                            SyntaxFactory.IdentifierName(Constants.ItemsName),
                            SyntaxFactory.BracketedArgumentList(_code.CreateSeparatedList(SyntaxFactory.Argument(SyntaxFactory.IdentifierName("_index"))))))
                }.Union((loopContent as BlockSyntax)?.Statements ?? (IEnumerable<StatementSyntax>) new[] {loopContent})));
        
        private StatementSyntax GetForEachStatement(StatementSyntax loopContent)
            => SyntaxFactory.ForEachStatement(
                SyntaxFactory.IdentifierName("var"),
                Constants.ItemName,
                SyntaxFactory.IdentifierName(Constants.ItemsName),
                loopContent is BlockSyntax ? loopContent : SyntaxFactory.Block(loopContent));
        
        private MethodDeclarationSyntax GetCoreMethod(TypeSyntax returnType, IEnumerable<StatementSyntax> prologue,
            IEnumerable<StatementSyntax> epilogue, string functionName, IEnumerable<ParameterSyntax> parameters,
            ITypeSymbol? collectionSemanticType, StatementSyntax foreachStatement)
            => SyntaxFactory.MethodDeclaration(returnType, functionName)
                .WithParameterList(_code.CreateParameters(parameters))
                .WithBody(
                    SyntaxFactory.Block((collectionSemanticType.IsValueType
                            ? Enumerable.Empty<StatementSyntax>()
                            : new[]
                            {
                                SyntaxFactory.IfStatement(
                                    SyntaxFactory.BinaryExpression(SyntaxKind.EqualsExpression,
                                        SyntaxFactory.IdentifierName(Constants.ItemsName),
                                        SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
                                    _code.CreateThrowException("System.ArgumentNullException"))
                            })
                        .Concat(prologue)
                        .Concat(new[] { foreachStatement})
                        .Concat(epilogue)))
                .WithStatic(_data.CurrentMethodIsStatic)
                .WithTypeParameterList(_data.CurrentMethodTypeParameters)
                .WithConstraintClauses(_data.CurrentMethodConstraintClauses)
                .NormalizeWhitespace();
    }
}