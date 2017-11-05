using System;
using System.Diagnostics;

namespace GenerationAttributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    [Conditional("CodeGeneration")]
    public class CaseAttribute : Attribute
    {
        
    }

    [AttributeUsage(AttributeTargets.Class)]
    [Conditional("CodeGeneration")]
    public class MatcherAttribute : Attribute
    {

    }
}
