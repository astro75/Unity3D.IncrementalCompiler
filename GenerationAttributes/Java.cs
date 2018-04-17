using System;
using System.Diagnostics;

namespace GenerationAttributes
{
    [AttributeUsage(AttributeTargets.Interface)]
    [Conditional("CodeGeneration")]
    public class JavaListenerInterfaceAttribute : Attribute
    {
        public readonly string Module;

        public JavaListenerInterfaceAttribute(string module) {
            Module = module;
        }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
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
        public readonly string Imports;
        public readonly string ClassBody;

        public JavaClassAttribute(string module, string imports, string classBody) {
            Module = module;
            Imports = imports;
            ClassBody = classBody;
        }
    }

    public class JavaFile
    {
        public readonly string Module, Path, Contents;

        public JavaFile(string module, string path, string contents) {
            Module = module;
            Path = path;
            Contents = contents;
        }
    }
}
