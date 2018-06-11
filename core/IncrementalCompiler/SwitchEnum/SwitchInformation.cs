using System.Collections.Immutable;
using System.Linq;

namespace IncrementalCompiler.SwitchAnalyzer
{
    public sealed class SwitchInformation
    {
        public readonly ImmutableArray<string> NotFoundSymbolNames;
        readonly bool HasDefault;
        readonly bool DefaultThrows;

        public bool UnreachableDefault => HasDefault && NotFoundSymbolNames.Any() == false && !DefaultThrows;
        public bool NotExhaustiveSwitch => NotFoundSymbolNames.Any() && (!HasDefault || DefaultThrows);

        public SwitchInformation(ImmutableArray<string> notFoundSymbolNames, bool hasDefault, bool defaultThrows)
        {
            NotFoundSymbolNames = notFoundSymbolNames;
            HasDefault = hasDefault;
            DefaultThrows = defaultThrows;
        }
    }
}
