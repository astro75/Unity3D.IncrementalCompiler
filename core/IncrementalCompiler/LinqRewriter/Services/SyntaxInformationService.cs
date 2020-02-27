using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shaman.Roslyn.LinqRewrite.DataStructures;

namespace Shaman.Roslyn.LinqRewrite.Services
{
    public class SyntaxInformationService
    {
        private static SyntaxInformationService _instance;
        public static SyntaxInformationService Instance => _instance ??= new SyntaxInformationService();

        private readonly RewriteDataService _data;
        public SyntaxInformationService()
        {
            _data = RewriteDataService.Instance;
        }
        
        public ITypeSymbol GetSymbolType(ISymbol x)
        {
            switch (x)
            {
                case ILocalSymbol local: return local.Type;
                case IParameterSymbol param: return param.Type;
                default: throw new NotImplementedException();
            }
        }
        
        public ITypeSymbol GetSymbolType(VariableCapture x) 
            => GetSymbolType(x.Symbol);

        public string GetMethodFullName(InvocationExpressionSyntax invocation)
        {
            var n = (_data.Semantic.GetSymbolInfo(invocation.Expression).Symbol as IMethodSymbol)?.OriginalDefinition
                .ToDisplayString();
            const string iEnumerableOfTSource = "System.Collections.Generic.IEnumerable<TSource>";

            return n?.Replace("System.Collections.Generic.List<TSource>", iEnumerableOfTSource)
                .Replace("TSource[]", iEnumerableOfTSource);
        }

        public ITypeSymbol GetItemType(ITypeSymbol collectionType)
            => collectionType is IArrayTypeSymbol symbol
                ? symbol.ElementType
                : collectionType.AllInterfaces
                    .Concat(new[] {collectionType})
                    .OfType<INamedTypeSymbol>()
                    .FirstOrDefault(x => x.IsGenericType && x.ConstructUnboundGenericType().ToString() == "System.Collections.Generic.IEnumerable<>")?
                    .TypeArguments.First();

        public ITypeSymbol GetLambdaReturnType(AnonymousFunctionExpressionSyntax lambda)
        {
            var symbol = ((INamedTypeSymbol) _data.Semantic.GetTypeInfo(lambda).ConvertedType).TypeArguments.Last();
            return symbol;
        }

        public ParameterSyntax GetLambdaParameter(Lambda lambda, int index)
            => lambda.Parameters[index];

        public string GetUniqueName(string v)
        {
            for (var i = 1;; i++)
            {
                var name = v + i;
                if (_data.MethodsToAddToCurrentType.Any(x => x.Item2.Identifier.ValueText == name)) continue;
                return name;
            }
        }
    }
}