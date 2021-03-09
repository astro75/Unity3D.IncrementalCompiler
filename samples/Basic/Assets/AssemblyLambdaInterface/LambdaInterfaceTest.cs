using GenerationAttributes;

[LambdaInterface]
public interface Test<A, B> where A : struct { 
  int add(int a, int b);
  int negate(int a);
  void print(string s);
  A identity(A a);
  (A, B) zip(A a, B b);
}

[LambdaInterface] public interface IExampleInterface {
  int add(int a, int b);
  int negate(int a);
}

namespace System.Runtime.CompilerServices {
  public class IsExternalInit{}
}