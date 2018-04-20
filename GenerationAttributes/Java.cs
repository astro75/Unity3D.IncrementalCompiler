using System;
using System.Diagnostics;

namespace GenerationAttributes
{
    [AttributeUsage(AttributeTargets.Interface)]
    [Conditional("CodeGeneration")]
    public class JavaListenerInterfaceAttribute : Attribute
    {
        public readonly string Module;

        /// <summary>
        /// When added on a C# interface [CS_interface]:
        /// 1. Generates identical interface in java with all the methods.
        /// 2. Generates a class named [CS_interface]Proxy that implements the generated java interface.
        /// Generated class extends tlplib.JavaListenerProxy
        /// </summary>
        /// <param name="module">Android module the code will be generated in</param>
        public JavaListenerInterfaceAttribute(string module) {
            Module = module;
        }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
    [Conditional("CodeGeneration")]
    public class JavaMethodAttribute : Attribute
    {
        public readonly string MethodBody;

        /// <summary>
        /// Can only be used inside class with JavaClass attribute.
        /// Generates a method inside that java class.
        /// Method should not have an implementation. Compiler generates C# method body at compile-time.
        /// When you call this method in C#, it gets redirected to Java automatically.
        /// </summary>
        /// <param name="methodBody">Method body in generated Java code.</param>
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

        /// <summary>
        /// Generates a java class with provided imports and body.
        /// When you instantiate this object, then a mirror object is also instantiated in Java.
        /// You should use <see cref="JavaMethodAttribute"/> on methods inside this class.
        /// </summary>
        /// <param name="module">Android module the code will be generated in</param>
        /// <param name="imports">Java code that will be put at top of the generated file</param>
        /// <param name="classBody">Java code that will be put in the generated class</param>
        public JavaClassAttribute(string module, string imports, string classBody) {
            Module = module;
            Imports = imports;
            ClassBody = classBody;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    [Conditional("CodeGeneration")]
    public class JavaBindingAttribute : Attribute
    {
        public readonly string JavaClass;

        /// <summary>
        /// Use this on classes that extend Binding, AndroidJavaObject and AndroidJavaProxy.
        /// This attribute is used by <see cref="JavaMethodAttribute"/> to determine what object type this is in Java
        /// when generating code.
        /// </summary>
        /// <param name="javaClass">Full java class name of associated java object</param>
        public JavaBindingAttribute(string javaClass) {
            JavaClass = javaClass;
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
