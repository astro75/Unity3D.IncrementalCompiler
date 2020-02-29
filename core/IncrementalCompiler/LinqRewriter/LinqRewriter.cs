using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalCompiler;
using Shaman.Roslyn.LinqRewrite.DataStructures;
using Shaman.Roslyn.LinqRewrite.Services;
using SyntaxExtensions = Shaman.Roslyn.LinqRewrite.Extensions.SyntaxExtensions;

namespace Shaman.Roslyn.LinqRewrite
{
    public class LinqRewriter : CSharpSyntaxRewriter
    {
        private readonly RewriteDataService _data;
        private readonly SyntaxInformationService _info;
        private readonly CodeCreationService _code;
        readonly RewriteService _rewriteService;

        public int RewrittenMethods { get; private set; }
        public int RewrittenLinqQueries { get; private set; }

        public LinqRewriter(SemanticModel semantic)
        {
            _data = new RewriteDataService();
            _info = new SyntaxInformationService(_data);
            _code = new CodeCreationService(_data, _info);
            var processingStep = new ProcessingStepCreationService(_data, _code);

            _data.Semantic = semantic;
            _rewriteService = new RewriteService(_data, _info, _code, processingStep, Visit);
        }
        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
            => TryVisitInvocationExpression(node, null) ?? base.VisitInvocationExpression(node);

        private bool insideConditionalExpression;
        public override SyntaxNode VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            var old = insideConditionalExpression;
            insideConditionalExpression = true;
            try
            {
                return base.VisitConditionalAccessExpression(node);
            }
            finally
            {
                insideConditionalExpression = old;
            }
        }

        public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
            => VisitTypeDeclaration(node);

        public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
            => TryVisitForEachStatement(node) ?? base.VisitForEachStatement(node);

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            // if (HasNoRewriteAttribute(node.AttributeLists)) return node;
            // var old = RewrittenLinqQueries;
            // var syntaxNode = base.VisitMethodDeclaration(node);
            // if (RewrittenLinqQueries != old) RewrittenMethods++;
            // return syntaxNode;


            if (HasNoRewriteAttribute(node.AttributeLists)) return node;

            var rootMethod = false;
            if (!_data.InMethod)
            {
                _data.InMethod = true;
                rootMethod = true;
            }

            _data.CurrentTypes.Push(node);
            var changed = (MethodDeclarationSyntax) base.VisitMethodDeclaration(node);

            // TODO: probably don't need to check for methods if we do that for blocks

            if (_data.MethodsToAddToCurrentType.Count != 0)
            {
                var newMembers = _data.MethodsToAddToCurrentType
                    .Where(x => x.Item1 == node)
                    .Select(x => x.Item2)
                    .ToArray();

                if (changed.ExpressionBody != null)
                {
                    // replace `=> x;` with `{ (return) x; }`
                    var isVoid = _data.Semantic.GetDeclaredSymbol(node)?.ReturnType.SpecialType == SpecialType.System_Void;
                    changed = changed.WithExpressionBody(null).WithBody(SyntaxFactory.Block(
                        isVoid
                            ? (StatementSyntax) SyntaxFactory.ExpressionStatement(changed.ExpressionBody.Expression)
                            : SyntaxFactory.ReturnStatement(changed.ExpressionBody.Expression)
                    ));
                }

                var withMethods = changed.AddBodyStatements(newMembers.Select(_ =>
                    (StatementSyntax) SyntaxFactory.LocalFunctionStatement(
                        _.Modifiers.RemoveOfKind(SyntaxKind.StaticKeyword), _.ReturnType, _.Identifier, _.TypeParameterList, _.ParameterList,
                        _.ConstraintClauses, _.Body, _.ExpressionBody)
                ).ToArray());

                _data.MethodsToAddToCurrentType.RemoveAll(x => x.Item1 == node);
                clean();
                return withMethods.NormalizeWhitespace();
            }
            clean();
            return changed;

            void clean() {
                _data.CurrentTypes.Pop();
                if (rootMethod)
                {
                    _data.InMethod = false;
                    _data.UsedNames.Clear();
                }
            }
        }

        public override SyntaxNode VisitBlock(BlockSyntax node) {
            // TODO: HasNoRewriteAttribute
            // TODO: remove code duplication

            _data.CurrentTypes.Push(node);
            var changed = (BlockSyntax) base.VisitBlock(node);

            if (_data.MethodsToAddToCurrentType.Count != 0)
            {
                var newMembers = _data.MethodsToAddToCurrentType
                    .Where(x => x.Item1 == node)
                    .Select(x => x.Item2)
                    .ToArray();

                if (newMembers.Length > 0)
                {
                    var withMethods = changed.AddStatements(newMembers.Select(_ =>
                        (StatementSyntax) SyntaxFactory.LocalFunctionStatement(
                            _.Modifiers.RemoveOfKind(SyntaxKind.StaticKeyword), _.ReturnType, _.Identifier,
                            _.TypeParameterList, _.ParameterList,
                            _.ConstraintClauses, _.Body, _.ExpressionBody)
                    ).ToArray());

                    _data.MethodsToAddToCurrentType.RemoveAll(x => x.Item1 == node);
                    _data.CurrentTypes.Pop();
                    return withMethods.NormalizeWhitespace();
                }
            }
            _data.CurrentTypes.Pop();
            return changed;
        }

        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) {
            _data.CurrentTypes.Push(node);
            var changed = (SimpleLambdaExpressionSyntax) base.VisitSimpleLambdaExpression(node);

            if (_data.MethodsToAddToCurrentType.Count != 0)
            {
                var newMembers = _data.MethodsToAddToCurrentType
                    .Where(x => x.Item1 == node)
                    .Select(x => x.Item2)
                    .ToArray();

                if (newMembers.Length > 0)
                {
                    // body is not block, because we handle them in VisitBlock

                    var isVoid = ((IMethodSymbol) _data.Semantic.GetSymbolInfo(node).Symbol).ReturnType.SpecialType == SpecialType.System_Void;
                    var newBody = SyntaxFactory.Block(
                        isVoid
                            ? (StatementSyntax) SyntaxFactory.ExpressionStatement((ExpressionSyntax) changed.Body)
                            : SyntaxFactory.ReturnStatement((ExpressionSyntax) changed.Body)
                    );

                    var withMethods = changed.WithBody(newBody.AddStatements(newMembers.Select(_ =>
                        (StatementSyntax) SyntaxFactory.LocalFunctionStatement(
                            _.Modifiers.RemoveOfKind(SyntaxKind.StaticKeyword), _.ReturnType, _.Identifier,
                            _.TypeParameterList, _.ParameterList,
                            _.ConstraintClauses, _.Body, _.ExpressionBody)
                    ).ToArray()));

                    _data.MethodsToAddToCurrentType.RemoveAll(x => x.Item1 == node);
                    _data.CurrentTypes.Pop();
                    return withMethods.NormalizeWhitespace();
                }
            }
            _data.CurrentTypes.Pop();
            return changed;
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            => VisitTypeDeclaration(node);

        private ExpressionSyntax TryVisitInvocationExpression(InvocationExpressionSyntax node, ForEachStatementSyntax containingForEach)
        {
            if (insideConditionalExpression) return null;
            var methodIdx = _data.MethodsToAddToCurrentType.Count;
            try
            {
                var expressionSyntax = VisitInvocationExpression(node, containingForEach);
                if (expressionSyntax != null)
                {
                    RewrittenLinqQueries++;
                    return expressionSyntax;
                }
            }
            catch (Exception ex) when (ex is InvalidCastException || ex is NotSupportedException)
            {
                _data.MethodsToAddToCurrentType.RemoveRange(methodIdx, _data.MethodsToAddToCurrentType.Count - methodIdx);
            }
            return null;
        }

        private ExpressionSyntax VisitInvocationExpression(InvocationExpressionSyntax node,
            ForEachStatementSyntax containingForEach)
        {
            if (!(node.Expression is MemberAccessExpressionSyntax)) return null;

            //var symbol = _data.Semantic.GetSymbolInfo(memberAccess).Symbol as IMethodSymbol;
            // TODO: optimize
            var owner = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (owner == null) return null;
            if (!IsSupportedMethod(node)) return null;

            // _data.CurrentMethodIsStatic = _data.Semantic.GetDeclaredSymbol(owner).IsStatic;
            _data.CurrentMethodName = owner.Identifier.ValueText;
            //_data.CurrentMethodTypeParameters = ((MethodDeclarationSyntax) owner).TypeParameterList;
            //_data.CurrentMethodConstraintClauses = ((MethodDeclarationSyntax) owner).ConstraintClauses;

            var chain = GetInvocationStepChain(node, out var lastNode);
            if (containingForEach != null) InvocationChainInsertForEach(chain, containingForEach);

            var (rewrite, collection, semanticReturnType) = CheckIfRewriteInvocation(chain, node, lastNode);
            if (!rewrite) return null;

            var returnType = SyntaxFactory.ParseTypeName(semanticReturnType.ToDisplayString());
            var aggregationMethod = chain.First().MethodName;

            using var parameters = RewriteParametersHolder.BorrowParameters(_rewriteService, _code, _data, _info);
            parameters.SetData(aggregationMethod, collection, returnType, semanticReturnType, chain, node);

            return InvocationRewriter.TryRewrite(parameters, aggregationMethod)
                .WithLeadingTrivia(((CSharpSyntaxNode) containingForEach ?? node).GetLeadingTrivia())
                .WithTrailingTrivia(((CSharpSyntaxNode) containingForEach ?? node).GetTrailingTrivia());
        }

        private SyntaxNode VisitTypeDeclaration(TypeDeclarationSyntax node)
        {
            if (HasNoRewriteAttribute(node.AttributeLists)) return node;

            _data.CurrentTypes.Push(node);
            var changed = (TypeDeclarationSyntax) (node is ClassDeclarationSyntax declarationSyntax
                ? base.VisitClassDeclaration(declarationSyntax)
                : base.VisitStructDeclaration((StructDeclarationSyntax) node));

            if (_data.MethodsToAddToCurrentType.Count != 0)
            {
                var newMembers = _data.MethodsToAddToCurrentType
                    .Where(x => x.Item1 == node)
                    .Select(x => x.Item2)
                    .ToArray();

                var withMethods = changed is ClassDeclarationSyntax syntax
                    ? (TypeDeclarationSyntax) syntax.AddMembers(newMembers)
                    : ((StructDeclarationSyntax) changed).AddMembers(newMembers);

                _data.MethodsToAddToCurrentType.RemoveAll(x => x.Item1 == node);
                _data.CurrentTypes.Pop();
                return withMethods.NormalizeWhitespace();
            }
            _data.CurrentTypes.Pop();
            return changed;
        }

        private SyntaxNode TryVisitForEachStatement(ForEachStatementSyntax node)
        {
            if (!(node.Expression is InvocationExpressionSyntax collection) || !IsSupportedMethod(collection))
                return base.VisitForEachStatement(node);

            var visitor = new CanReWrapForeachVisitor();
            visitor.Visit(node.Statement);
            if (visitor.Fail) return base.VisitForEachStatement(node);

            var expressionSyntax = TryVisitInvocationExpression(collection, node);
            return expressionSyntax != null
                ? SyntaxFactory.ExpressionStatement(expressionSyntax)
                : base.VisitForEachStatement(node);
        }

        private List<LinqStep> GetInvocationStepChain(InvocationExpressionSyntax node, out InvocationExpressionSyntax lastNode)
        {
            var chain = new List<LinqStep>
            {
                new LinqStep(_info.GetMethodFullName(node),
                    node.ArgumentList.Arguments.Select(x => x.Expression).ToArray(), node)
            };
            lastNode = node;
            while (node.Expression is MemberAccessExpressionSyntax syntax)
            {
                node = syntax.Expression as InvocationExpressionSyntax;
                if (node != null && IsSupportedMethod(node))
                {
                    chain.Add(new LinqStep(_info.GetMethodFullName(node),
                        node.ArgumentList.Arguments.Select(x => x.Expression).ToArray(), node));
                    lastNode = node;
                }
                else break;
            }
            return chain;
        }

        private (bool, ExpressionSyntax, ITypeSymbol) CheckIfRewriteInvocation(List<LinqStep> chain, InvocationExpressionSyntax node, InvocationExpressionSyntax lastNode)
        {
            // if (!chain.Any(x => x.Arguments
            //     .Any(y => y is AnonymousFunctionExpressionSyntax)))
            //     return (false, null, null);

            if (chain.Count == 1 && Constants.RootMethodsThatRequireYieldReturn.Contains(chain[0].MethodName))
                return (false, null, null);

            // var (flowsIn, flowsOut) = GetFlows(chain);
            _data.CurrentFlow = new VariableCapture[0];
            // _data.CurrentFlow = flowsIn.Union(flowsOut)
            //     .Where(x => (x as IParameterSymbol)?.IsThis != true)
            //     .Select(x => _code.CreateVariableCapture(x, flowsOut));

            var collection = ((MemberAccessExpressionSyntax) lastNode.Expression).Expression;
            if (SyntaxExtensions.IsAnonymousType(_data.Semantic.GetTypeInfo(collection).Type)) return (false, null, null);

            var semanticReturnType = _data.Semantic.GetTypeInfo(node).Type;
            if (SyntaxExtensions.IsAnonymousType(semanticReturnType) ||
                _data.CurrentFlow.Any(x => SyntaxExtensions.IsAnonymousType(_info.GetSymbolType(x.Symbol))))
                return (false, null, null);

            return (true, collection, semanticReturnType);
        }

        private void InvocationChainInsertForEach(List<LinqStep> chain, ForEachStatementSyntax forEach)
        {
            chain.Insert(0,
                new LinqStep(Constants.EnumerableForEachMethod,
                    new ExpressionSyntax[]
                    {
                        SyntaxFactory.SimpleLambdaExpression(
                            SyntaxFactory.Parameter(forEach.Identifier), forEach.Statement)
                    })
                {
                    Lambda = new Lambda(forEach.Statement,
                        new[]
                        {
                            _code.CreateParameter(forEach.Identifier,
                                _data.Semantic.GetTypeInfo(forEach.Type).ConvertedType)
                        })
                });
        }

        private (List<ISymbol>, List<ISymbol>) GetFlows(List<LinqStep> chain)
        {
            var flowsIn = new List<ISymbol>();
            var flowsOut = new List<ISymbol>();
            foreach (var item in chain)
            {
                foreach (var syntax in item.Arguments)
                {
                    if (item.Lambda != null)
                    {
                        var dataFlow = _data.Semantic.AnalyzeDataFlow(item.Lambda.Body);
                        var pName = item.Lambda.Parameters.Single().Identifier.ValueText;
                        foreach (var k in dataFlow.DataFlowsIn)
                        {
                            if (k.Name == pName) continue;
                            if (!flowsIn.Contains(k)) flowsIn.Add(k);
                        }
                        foreach (var k in dataFlow.DataFlowsOut)
                        {
                            if (k.Name == pName) continue;
                            if (!flowsOut.Contains(k)) flowsOut.Add(k);
                        }
                    }
                    else
                    {
                        var dataFlow = _data.Semantic.AnalyzeDataFlow(syntax);
                        foreach (var k in dataFlow.DataFlowsIn)
                            if (!flowsIn.Contains(k)) flowsIn.Add(k);

                        foreach (var k in dataFlow.DataFlowsOut)
                            if (!flowsOut.Contains(k)) flowsOut.Add(k);
                    }
                }
            }
            return (flowsIn, flowsOut);
        }

        private bool IsSupportedMethod(InvocationExpressionSyntax invocation)
        {
            var name = _info.GetMethodFullName(invocation);
            if (!IsSupportedMethod(name)) return false;
            return true;
            //if (invocation.ArgumentList.Arguments.Count == 0) return true;
            //if (name == Constants.ElementAtMethod || name == Constants.ElementAtOrDefaultMethod || name == Constants.ContainsMethod) return true;

            // Passing things like .Select(Method) is not supported.
            //return invocation.ArgumentList.Arguments.All(x => x.Expression is AnonymousFunctionExpressionSyntax);
        }

        private static bool IsSupportedMethod(string v)
        {
            if (v == null) return false;
            if (Constants.KnownMethods.Contains(v)) return true;

            if (!v.StartsWith("System.Collections.Generic.IEnumerable<")) return false;
            var k = v.Replace("<", "(");

            if (!k.Contains(">.Sum(") && !k.Contains(">.Average(") && !k.Contains(">.Min(") &&
                !k.Contains(">.Max(")) return false;

            if (k.Contains("TResult")) return false;
            if (v == "System.Collections.Generic.IEnumerable<TSource>.Min()") return false;
            if (v == "System.Collections.Generic.IEnumerable<TSource>.Max()") return false;
            return true;
        }

        private bool HasNoRewriteAttribute(SyntaxList<AttributeListSyntax> attributeLists) =>
            attributeLists.Any(x =>
                x.Attributes.Any(y =>
            {
                var symbol = ((IMethodSymbol) _data.Semantic.GetSymbolInfo(y).Symbol).ContainingType;
                return symbol.ToDisplayString() == "Shaman.Runtime.NoLinqRewriteAttribute";
            }));

        public Diagnostic CreateDiagnosticForException(Exception ex, string path)
        {
            while (ex.InnerException != null)
                ex = ex.InnerException;

            var message = $"roslyn-linq-rewrite exception while processing '{path}', method {_data.CurrentMethodName}: {ex.Message} -- {ex.StackTrace?.Replace("\n", "")}";
            return Diagnostic.Create("LQRW1001", "Compiler", new LiteralString(message), DiagnosticSeverity.Error,
                DiagnosticSeverity.Error, true, 0);
        }
    }
}
