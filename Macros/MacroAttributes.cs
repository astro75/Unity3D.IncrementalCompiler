using System;
using System.Diagnostics;

namespace GenerationAttributes
{
    [AttributeUsage(AttributeTargets.Method)]
    [Conditional("CodeGeneration")]
    public class SimpleMethodMacro : Attribute
    {
        public string Pattern { get; set; }

        public SimpleMethodMacro(string pattern) {
            Pattern = pattern;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    [Conditional("CodeGeneration")]
    public class VarMethodMacro : Attribute
    {
        public string Pattern { get; set; }

        public VarMethodMacro(string pattern) {
            Pattern = pattern;
        }
    }
}
