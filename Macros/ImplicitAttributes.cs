using System;
using System.Diagnostics;
using JetBrains.Annotations;

namespace GenerationAttributes
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field)]
    [MeansImplicitUse]
    public class Implicit : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    [Conditional(Consts.UNUSED_NAME)]
    public class ImplicitPassThrough : Attribute { }

    static class Consts {
      public const string UNUSED_NAME = "CodeGeneration";
    }
}
