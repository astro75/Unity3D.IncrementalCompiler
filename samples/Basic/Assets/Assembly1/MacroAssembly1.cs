using GenerationAttributes;

public static class MacroAssembly1
{
    [SimpleMethodMacro("\"${number}\"")]
    public static string macro1(int number) => throw new MacroException();

    public static void test() {
        var s = macro1(42);
        // Generic2<int, bool>.NesetedGeneric<string>.withImplicit();
        // GenericNot.withImplicit();
    }
}

public class GenericNot {
  public static int withImplicit([Implicit] int x = default) {
    return x;
  }
}

public class Generic<A> {
  public static int withImplicit([Implicit] int x = default) {
    return x;
  }

  public static A withImplicit2([Implicit] int x = default) {
    return default;
  }
}

public class Generic2<A, B> {
  public static int withImplicit([Implicit] int x = default) {
    return x;
  }

  public static A withImplicit2([Implicit] int x = default) {
    return default;
  }

  public class NesetedGeneric<C> {
    public static int withImplicit([Implicit] int x = default) {
      return x;
    }
  }
}
