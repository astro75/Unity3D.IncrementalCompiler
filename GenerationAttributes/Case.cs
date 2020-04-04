using System;
using System.Diagnostics;

namespace GenerationAttributes
{
    /// <summary>
    /// <code><![CDATA[
    ///    None = 0,
    ///    Constructor = 1,
    ///    Apply =   Constructor | 1 << 1,
    ///    Copy =    Constructor | 1 << 2,
    ///    Withers = Constructor | 1 << 3,
    ///    Default = Constructor | Copy | Withers,
    ///    All =     Constructor | Apply | Copy | Withers
    /// ]]></code>
    /// </summary>
    [Flags]
    public enum ConstructorFlags
    {
        None = 0,
        Constructor = 1,
        Apply =   Constructor | 1 << 1,
        Copy =    Constructor | 1 << 2,
        Withers = Constructor | 1 << 3,
        Default = Constructor | Copy | Withers,
        All =     Constructor | Apply | Copy | Withers
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    [Conditional(Consts.UNUSED_NAME)]
    public class RecordAttribute : Attribute
    {
        public bool GenerateToString { get; set; } = true;
        public bool GenerateComparer { get; set; } = true;
        public bool GenerateGetHashCode { get; set; } = true;
        // Can't use nullable in attributes
        public ConstructorFlags GenerateConstructor { get; set; } = ConstructorFlags.Default;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
    [Conditional(Consts.UNUSED_NAME)]
    public class MatcherAttribute : Attribute
    {
        public string ClassName { get; set; } = null;
    }

    [AttributeUsage(AttributeTargets.Field)]
    [Conditional(Consts.UNUSED_NAME)]
    public class PublicAccessor : Attribute
    {

    }

    [AttributeUsage(AttributeTargets.Class)]
    [Conditional(Consts.UNUSED_NAME)]
    public class SingletonAttribute : Attribute { }

    static class Consts
    {
        public const string UNUSED_NAME = "CodeGeneration";
    }
}
