using System;
using System.Diagnostics;

namespace GenerationAttributes
{
    [AttributeUsage(AttributeTargets.Interface)]
    [Conditional("CodeGeneration")]
    public class JavaInterfaceAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    [Conditional("CodeGeneration")]
    public class JavaMethodAttribute : Attribute
    {
        public readonly string MethodBody;

        public JavaMethodAttribute(string methodBody) {
            MethodBody = methodBody;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    [Conditional("CodeGeneration")]
    public class JavaClassAttribute : Attribute
    {
        public readonly string Module;
        public readonly string ClassBody;

        public JavaClassAttribute(string module, string classBody) {
            Module = module;
            ClassBody = classBody;
        }
    }
}
