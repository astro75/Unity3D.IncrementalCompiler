using System;

namespace GenerationAttributes
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field)]
    public class Implicit : Attribute
    {
    }
}
