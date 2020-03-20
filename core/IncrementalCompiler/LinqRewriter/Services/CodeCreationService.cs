using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MonadLib;
using Shaman.Roslyn.LinqRewrite.DataStructures;
using Shaman.Roslyn.LinqRewrite.Extensions;
using SyntaxExtensions = Shaman.Roslyn.LinqRewrite.Extensions.SyntaxExtensions;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Shaman.Roslyn.LinqRewrite.Services
{
    public class CodeCreationService
    {
        // private static CodeCreationService _instance;
        // public static CodeCreationService Instance => _instance ??= new CodeCreationService();

        private readonly RewriteDataService _data;
        private readonly SyntaxInformationService _info;

        public CodeCreationService(RewriteDataService data, SyntaxInformationService info) {
            _data = data;
            _info = info;
        }

        public StatementSyntax CreateStatement(ExpressionSyntax expression)
            => SyntaxFactory.ExpressionStatement(expression);

        public ThrowStatementSyntax CreateThrowException(string type, string message = null)
            => SyntaxFactory.ThrowStatement(
                SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.ParseTypeName(type),
                    CreateArguments(message != null
                        ? new ExpressionSyntax[]
                        {
                            SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                                SyntaxFactory.Literal(message))
                        }
                        : new ExpressionSyntax[] { }), null));

        public LocalDeclarationStatementSyntax CreateLocalVariableDeclaration(string name, ExpressionSyntax value)
            => SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                SyntaxFactory.IdentifierName("var"),
                CreateSeparatedList(SyntaxFactory.VariableDeclarator(name).WithInitializer(SyntaxFactory.EqualsValueClause(value)))));

        public SeparatedSyntaxList<T> CreateSeparatedList<T>(IEnumerable<T> items) where T : SyntaxNode
            => SyntaxFactory.SeparatedList(items);

        public SeparatedSyntaxList<T> CreateSeparatedList<T>(params T[] items) where T : SyntaxNode
            => SyntaxFactory.SeparatedList(items);

        public ArgumentListSyntax CreateArguments(IEnumerable<ExpressionSyntax> items)
            => CreateArguments(items.Select(x => SyntaxFactory.Argument(x)));

        public ArgumentListSyntax CreateArguments(params ExpressionSyntax[] items)
            => CreateArguments((IEnumerable<ExpressionSyntax>) items);

        public ArgumentListSyntax CreateArguments(IEnumerable<ArgumentSyntax> items)
            => SyntaxFactory.ArgumentList(CreateSeparatedList(items));

        public ParameterListSyntax CreateParameters(IEnumerable<ParameterSyntax> items)
            => SyntaxFactory.ParameterList(CreateSeparatedList(items));

        public ParameterSyntax CreateParameter(SyntaxToken name, ITypeSymbol type)
            => SyntaxFactory.Parameter(name).WithType(SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));

        public ParameterSyntax CreateParameter(SyntaxToken name, TypeSyntax type)
            => SyntaxFactory.Parameter(name).WithType(type);

        public ParameterSyntax CreateParameter(string name, ITypeSymbol type)
            => CreateParameter(SyntaxFactory.Identifier(name), type);

        public ParameterSyntax CreateParameter(string name, TypeSyntax type)
            => CreateParameter(SyntaxFactory.Identifier(name), type);

        public PredefinedTypeSyntax CreatePrimitiveType(SyntaxKind keyword)
            => SyntaxFactory.PredefinedType(SyntaxFactory.Token(keyword));

        public ExpressionSyntax CreateCollectionCount(ExpressionSyntax collection, bool allowUnknown, int uniqueCounter)
        {
            var collectionType = _data.Semantic.GetTypeInfo(collection).Type;
            if (collectionType is IArrayTypeSymbol) return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(Constants.ItemsName + uniqueCounter), SyntaxFactory.IdentifierName("Length"));

            // if (collectionType.ToDisplayString().StartsWith("System.Collections.Generic.List<"))
            //     return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(Constants.ItemsName), SyntaxFactory.IdentifierName("Count"));

            if (collectionType.ToDisplayString().StartsWith("System.Collections.Generic.IReadOnlyCollection<") || collectionType.AllInterfaces.Any(x => x.ToDisplayString().StartsWith("System.Collections.Generic.IReadOnlyCollection<")))
                return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(Constants.ItemsName + uniqueCounter), SyntaxFactory.IdentifierName("Count"));

            if (collectionType.ToDisplayString().StartsWith("System.Collections.Generic.ICollection<") || collectionType.AllInterfaces.Any(x => x.ToDisplayString().StartsWith("System.Collections.Generic.ICollection<")))
                return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(Constants.ItemsName + uniqueCounter), SyntaxFactory.IdentifierName("Count"));

            if (!allowUnknown) return null;
            if (collectionType.IsValueType) return null;
            var itemType = _info.GetItemType(collectionType);
            if (itemType == null) return null;

            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ParenthesizedExpression(
                        SyntaxFactory.ConditionalAccessExpression(
                            SyntaxFactory.ParenthesizedExpression(
                                SyntaxFactory.BinaryExpression(
                                    SyntaxKind.AsExpression,
                                    SyntaxFactory.IdentifierName(Constants.ItemsName + uniqueCounter),
                                    SyntaxFactory.ParseTypeName(
                                        $"System.Collections.Generic.ICollection<{itemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>")
                                )
                            ),
                            SyntaxFactory.MemberBindingExpression(SyntaxFactory.IdentifierName("Count"))
                        )
                    ),
                    SyntaxFactory.IdentifierName("GetValueOrDefault")
                )
            );
        }

        public ExpressionSyntax CreateMethodNameSyntaxWithCurrentTypeParameters(string fn)
            => (_data.CurrentMethodTypeParameters?.Parameters.Count).GetValueOrDefault() != 0
                ? SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(fn),
                    SyntaxFactory.TypeArgumentList(
                        CreateSeparatedList(_data.CurrentMethodTypeParameters.Parameters
                            .Select(x => SyntaxFactory.ParseTypeName(x.Identifier.ValueText)))))
                : (NameSyntax) SyntaxFactory.IdentifierName(fn);

        public (ICollection<StatementSyntax>, ExpressionSyntax) InlineOrCreateMethod(Lambda lambda, TypeSyntax returnType, ParameterSyntax param, bool isVoid)
        {
            // var p = _info.GetLambdaParameter(lambda, 0).Identifier.ValueText;
            //var lambdaParameter = semantic.GetDeclaredSymbol(p);
            // var currentFlow = _data.Semantic.AnalyzeDataFlow(lambda.Body);
            // var currentCaptures = currentFlow
            //     .DataFlowsOut
            //     .Union(currentFlow.DataFlowsIn)
            //     .Where(x => x.Name != p && (x as IParameterSymbol)?.IsThis != true)
            //     .Select(x => CreateVariableCapture(x, currentFlow.DataFlowsOut))
            //     .ToList();

            lambda = RenameSymbol(lambda, 0, param.Identifier.ValueText);
            return InlineOrCreateMethod(lambda.Body, returnType, param, isVoid);
        }

        public (ICollection<StatementSyntax>, ExpressionSyntax) InlineOrCreateMethod(CSharpSyntaxNode body, TypeSyntax returnType,
            ParameterSyntax param, bool isVoid
        ) {
            if (body is ExpressionSyntax syntax) return (Array.Empty<StatementSyntax>(), syntax);

            var name = $"{_data.CurrentMethodName}_ProceduralLinqHelper";
            var fn = _info.GetUniqueName(name);

            if (returnType == null) throw new NotSupportedException(); // Anonymous type

            var argument = SyntaxFactory.IdentifierName(param.Identifier.ValueText);

            var method = SyntaxFactory.MethodDeclaration(returnType, fn)
                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
                    new[] { param }
                )))
                .WithBody(body as BlockSyntax ?? (body is StatementSyntax statementSyntax
                              ? SyntaxFactory.Block(statementSyntax)
                              : SyntaxFactory.Block(SyntaxFactory.ReturnStatement((ExpressionSyntax) body))))
                .WithStatic(_data.UseStatic)
                .WithTypeParameterList(_data.CurrentMethodTypeParameters)
                .WithConstraintClauses(_data.CurrentMethodConstraintClauses)
                .NormalizeWhitespace();

            {
                var methodParameter = method.ParameterList.Parameters[0].Identifier;
                var mBody = method.Body;
                var gotoLabel = "goto_" + name;
                var tempVarName = "return_" + name;

                LocalDeclarationStatementSyntax maybeVar = null;
                if (!isVoid)
                {
                    maybeVar = SF.LocalDeclarationStatement(SF.VariableDeclaration(
                        returnType, SF.SingletonSeparatedList(SF.VariableDeclarator(tempVarName))));
                }

                var rewriter = new ReturnRewriter(gotoLabel, tempVarName);
                var edited = (BlockSyntax) rewriter.Visit(mBody);
                if (rewriter.addedGoto)
                {
                    edited = edited.AddStatements(SF.LabeledStatement(SF.Identifier(gotoLabel), SF.EmptyStatement()));
                }

                //var assignment = CreateLocalVariableDeclaration(methodParameter.Text, argument);

                //edited = edited.WithStatements(SF.List(new []{ (StatementSyntax) assignment }).AddRange(edited.Statements));

                var result = new List<StatementSyntax>();
                if (maybeVar != null) result.Add(maybeVar);
                result.Add(edited);
                return (result, SF.IdentifierName(tempVarName));
            }

            // _data.AddMethod(method);
            // return SyntaxFactory.InvocationExpression(
            //     CreateMethodNameSyntaxWithCurrentTypeParameters(fn),
            //     CreateArguments(new[] { SyntaxFactory.Argument(argument)}));
        }

        public List<LinqStep> MaybeAddFilter(List<LinqStep> chain, bool condition)
        {
            if (!condition) return chain;
            var lambda = (LambdaExpressionSyntax)chain.First().Arguments.FirstOrDefault();
            return InsertExpandedShortcutMethod(chain, Constants.WhereMethod, lambda);
        }

        public List<LinqStep> MaybeAddSelect(List<LinqStep> chain, bool condition)
        {
            if (!condition) return chain;
            var lambda = (LambdaExpressionSyntax)chain.First().Arguments.FirstOrDefault();
            return InsertExpandedShortcutMethod(chain, Constants.SelectMethod, lambda);
        }

        private List<LinqStep> InsertExpandedShortcutMethod(List<LinqStep> chain, string methodFullName, LambdaExpressionSyntax lambda)
        {
            var ch = chain.ToList();
            // var baseExpression = ((MemberAccessExpressionSyntax)chain.First().Expression).Expression;
            ch.Insert(1, new LinqStep(methodFullName, new[] { lambda }));
            return ch;
        }

        private Lambda RenameSymbol(Lambda container, int argIndex, string newname)
        {
            if (container.Syntax != null)
            {
                return new Lambda(SF.InvocationExpression(
                    container.Syntax,
                    SF.ArgumentList(SF.SingletonSeparatedList(SF.Argument(SF.IdentifierName(newname))))
                ), new[] {SF.Parameter(SF.Identifier(newname))});
            }

            var oldParameter = _info.GetLambdaParameter(container, argIndex).Identifier.ValueText;
            //var oldsymbol = semantic.GetDeclaredSymbol(oldparameter);

            var tokensToRename = container.Body.DescendantNodesAndSelf()
                .Where(x =>
            {
                var sem = _data.Semantic.GetSymbolInfo(x).Symbol;
                return sem != null && (sem is ILocalSymbol || sem is IParameterSymbol) && sem.Name == oldParameter;
                //  if (sem.Symbol == oldsymbol) return true;
            });
            var syntax = SyntaxFactory.ParenthesizedLambdaExpression(
                CreateParameters(container.Parameters
                    .Select((x, i) =>  i == argIndex ? SyntaxFactory.Parameter(SyntaxFactory.Identifier(newname)).WithType(x.Type) : x)),
                container.Body.ReplaceNodes(tokensToRename, (a, b) =>
                {
                    if (b is IdentifierNameSyntax ide) return ide.WithIdentifier(SyntaxFactory.Identifier(newname));
                    throw new NotImplementedException();
                }));
            return new Lambda(syntax);
            //var doc = project.GetDocument(docid);

            //var annot = new SyntaxAnnotation("RenamedLambda");
            //var annotated = container.WithAdditionalAnnotations(annot);
            //var root = project.GetDocument(docid).GetSyntaxRootAsync().Result.ReplaceNode(container, annotated).SyntaxTree;
            //var proj = project.GetDocument(docid).WithSyntaxRoot(root.GetRoot()).Project;
            //doc = proj.GetDocument(docid);
            //var syntaxTree = doc.GetSyntaxTreeAsync().Result;
            //var modifiedSemantic = proj.GetCompilationAsync().Result.GetSemanticModel(syntaxTree);
            //annotated = (AnonymousFunctionExpressionSyntax)doc.GetSyntaxRootAsync().Result.GetAnnotatedNodes(annot).First();
            //var parameter = GetLambdaParameter(annotated, 0);
            //var renamed = Renamer.RenameSymbolAsync(proj.Solution, modifiedSemantic.GetDeclaredSymbol(parameter), newname, null).Result;
            //annotated = (AnonymousFunctionExpressionSyntax)renamed.GetDocument(doc.Id).GetSyntaxRootAsync().Result.GetAnnotatedNodes(annot).First();
            //return annotated.WithoutAnnotations();
        }

        public VariableCapture CreateVariableCapture(ISymbol symbol, IReadOnlyList<ISymbol> flowsOut)
        {
            var changes = flowsOut.Contains(symbol);
            if (changes) return new VariableCapture(symbol, changes);

            if (!(symbol is ILocalSymbol local)) return new VariableCapture(symbol, changes);
            var type = local.Type;

            if (!type.IsValueType) return new VariableCapture(symbol, changes);

            // Pass big structs by ref for performance.
            var size = StructureExtensions.GetStructSize(type);
            if (size > Constants.MaximumSizeForByValStruct) changes = true;
            return new VariableCapture(symbol, changes);
        }
    }
}
