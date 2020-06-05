using System;
using JetBrains.Annotations;

namespace GenerationAttributes
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field)]
    [MeansImplicitUse]
    public class Implicit : Attribute
    {
    }
}
