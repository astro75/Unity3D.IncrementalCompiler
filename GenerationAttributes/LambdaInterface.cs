using System;
using System.Diagnostics;

namespace GenerationAttributes {
  /// <summary>
  /// Generates a Lambda implementation of the marked interface.
  /// </summary>
  ///
  /// <remarks>
  /// <para>A Lambda implementation is a class where each function in the interface is converted to a delegate.</para>
  ///
  /// <para>Thus an interface like this:</para>
  /// <code><![CDATA[
  /// public interface IExampleInterface {
  ///   int add(int a, int b);
  ///   int negate(int a);
  /// }
  /// ]]></code>
  ///
  /// <para>Would be converted to:</para>
  /// <code><![CDATA[
  /// public record LambdaIExampleInterface(
  ///   LambdaIExampleInterface._add add_,
  ///   LambdaIExampleInterface._negate negate_
  /// ) : IExampleInterface {
  ///   public delegate int _add(int a, int b);
  ///   public int add(int a, int b) => add_(a, b);
  /// 
  ///   public delegate int _negate(int a);
  ///   public int negate(int a) => negate_(a);
  /// }
  /// ]]></code>
  /// </remarks>
  [AttributeUsage(AttributeTargets.Interface), Conditional(Consts.UNUSED_NAME)]
  public sealed class LambdaInterfaceAttribute : Attribute {}
}