using GenerationAttributes;

public static class MacroAssembly2
{
    public static void test() {
        var s = MacroAssembly1.macro1(42);
    }

    [LazyProperty] public static string lazyTest {
        get { return "str"; }
    }

    [LazyProperty] public static string lazyTest2 => "str";

    [Implicit] static int xz;

    public static void x() {
      Generic2<int, bool>.NesetedGeneric<string>.withImplicit();
    }
}
