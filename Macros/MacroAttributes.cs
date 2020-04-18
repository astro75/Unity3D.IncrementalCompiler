using System;
using System.Diagnostics;

namespace GenerationAttributes
{
    // ReSharper disable once UnusedType.Global
    public class MacroException : Exception
    {
        public MacroException() : base("This method was supposed to be replaced by a macro, but our compiler failed.") { }
    }

    /// <summary>
    /// Replaces call to a method annotated with this attribute with the specified string.
    /// </summary>
    ///
    /// Example usage (declaration):
    /// <code><![CDATA[
    /// public static class Util {
    ///   [SimpleMethodMacro(@"$""${obj}={${obj}}"""")]
    ///   public static string echo<A>(this A obj) => throw new MacroException();
    /// }
    /// ]]></code>
    ///
    /// Example usage (invocation):
    /// <code><![CDATA[
    /// var answer = 42;
    /// // prints "Our state: answer=42"
    /// Console.WriteLine($"Our state: {answer.echo()}");
    /// ]]></code>
    ///
    /// Parameters:
    /// <ul>
    /// <li>${this} - pointer to self. Not replaced in static methods.</li>
    /// <li>${argN} - N starts at 0 and refers to in argument list argument with index N</li>
    /// <li>${argumentName} - refers to argument named 'argumentName'</li>
    /// </ul>
    [AttributeUsage(AttributeTargets.Method)]
    public class SimpleMethodMacro : Attribute
    {
        public readonly string Pattern;

        public SimpleMethodMacro(string pattern) {
            Pattern = pattern;
        }
    }

    /// <summary>
    /// Replaces call to a method annotated with this attribute with the specified string.
    /// </summary>
    ///
    /// Example usage (declaration):
    /// <code><![CDATA[
    /// public class Log {
    ///   [StatementMethodMacro(@"if (${this}.isDebug()) ${this}.debugReal(${msg} /* or ${arg0} */);")]
    ///   public B debug(string msg) => throw new MacroException();
    /// }
    /// ]]></code>
    ///
    /// Example usage (invocation):
    /// <code><![CDATA[
    /// log.debug("I will only be logged in debug level!");
    /// ]]></code>
    ///
    /// Parameters:
    /// <ul>
    /// <li>${this} - pointer to self. Not replaced in static methods.</li>
    /// <li>${argN} - N starts at 0 and refers to in argument list argument with index N</li>
    /// <li>${argumentName} - refers to argument named 'argumentName'</li>
    /// </ul>
    [AttributeUsage(AttributeTargets.Method)]
    public class StatementMethodMacro : Attribute
    {
        public readonly string Pattern;

        public StatementMethodMacro(string pattern) {
            Pattern = pattern;
        }
    }

    /// <summary>
    /// Replaces local variable assignment from calling a method annotated with this attribute with the specified string.
    /// </summary>
    ///
    /// Example usage (declaration):
    /// <code><![CDATA[
    /// public class Either<A, B> {
    ///   [VarMethodMacro(
    /// @"var ${varName}__either = ${this};
    /// if (!${varName}__either.rightValueOut(out ${varType} ${varName})) return ${varName}__either.__unsafeGetLeft;
    /// ")]
    ///   public B rightOr_RETURN() => throw new MacroException();
    /// }
    ///
    /// publc static class EitherExts {
    ///   [VarMethodMacro(
    /// @"var ${varName}__either = ${either}; // or ${arg0}
    /// if (!${varName}__either.rightValueOut(out ${varType} ${varName})) return ${varName}__either.__unsafeGetLeft;
    /// ")]
    ///   public B rightOr_RETURN2<A, B>(this Either<A, B> either) => throw new MacroException();
    /// }
    /// ]]></code>
    ///
    /// Example usage (invocation):
    /// <code><![CDATA[
    /// var x = some.operation().rightOr_RETURN();
    /// ]]></code>
    ///
    /// Parameters:
    /// <ul>
    /// <li>${varType} - type of x</li>
    /// <li>${varName} - name of x</li>
    /// <li>${this} - pointer to self. Not replaced in static methods.</li>
    /// <li>${argN} - N starts at 0 and refers to in argument list argument with index N</li>
    /// <li>${argumentName} - refers to argument named 'argumentName'</li>
    /// </ul>
    [AttributeUsage(AttributeTargets.Method)]
    public class VarMethodMacro : Attribute
    {
        public readonly string Pattern;

        public VarMethodMacro(string pattern) {
            Pattern = pattern;
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    [Conditional("CodeGeneration")]
    public class LazyProperty : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class Inline : Attribute
    {
    }


    /// <summary>
    /// Used internally by compiler
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public class TypesWithMacroAttributes : Attribute
    {
        public readonly Type[] types;

        public TypesWithMacroAttributes(params Type[] types) {
            this.types = types;
        }
    }
}


