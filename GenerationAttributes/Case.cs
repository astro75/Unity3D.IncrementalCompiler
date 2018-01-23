using System;
using System.Diagnostics;

namespace GenerationAttributes
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    [Conditional("CodeGeneration")]
    public class RecordAttribute : Attribute
    {
        public bool GenerateToString { get; set; } = true;
        public bool GenerateComparer { get; set; } = true;
        public bool GenerateGetHashCode { get; set; } = true;
        public bool GenerateConstructor { get; set; } = true;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
    [Conditional("CodeGeneration")]
    public class MatcherAttribute : Attribute
    {

    }
}
