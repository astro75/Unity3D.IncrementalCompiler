using Microsoft.CodeAnalysis;

namespace Shaman.Roslyn.LinqRewrite.DataStructures
{
    public class VariableCapture
    {
        public VariableCapture(ISymbol symbol, bool changes)
        {
            Symbol = symbol;
            Changes = changes;
        }

        public ISymbol Symbol { get; }
        public bool Changes { get; }
        public string Name => Symbol.Name;
    }
}