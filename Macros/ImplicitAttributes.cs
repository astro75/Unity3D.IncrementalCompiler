using System;
using System.Diagnostics;
using JetBrains.Annotations;

namespace GenerationAttributes
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
    [MeansImplicitUse]
    public class Implicit : Attribute { }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
    [Conditional(Consts.UNUSED_NAME)]
    public class ImplicitPassThrough : Attribute { }

    static class Consts {
      public const string UNUSED_NAME = "CodeGeneration";
    }
}
