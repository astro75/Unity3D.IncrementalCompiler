using System;
using System.Diagnostics;

namespace GenerationAttributes
{
    public enum GeneratedContructor : byte
    {
        None,
        Constructor,
        ConstructorAndApply
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    [Conditional("CodeGeneration")]
    public class RecordAttribute : Attribute
    {
        public bool GenerateToString { get; set; } = true;
        public bool GenerateComparer { get; set; } = true;
        public bool GenerateGetHashCode { get; set; } = true;
        public GeneratedContructor GenerateConstructor { get; set; } = GeneratedContructor.Constructor;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
    [Conditional("CodeGeneration")]
    public class MatcherAttribute : Attribute
    {

    }

    [AttributeUsage(AttributeTargets.Field)]
    [Conditional("CodeGeneration")]
    public class PublicAccessor : Attribute
    {

    }
}
