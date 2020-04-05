using GenerationAttributes;

public static class MacroAssembly2
{
    public static void test()
    {
        var s = "42";
    }

    static string __lazy_value_lazyTest;
    [LazyProperty]
    static string __lazy_init_lazyTest
    {
        get
        {
            return "str";
        }
    }

    public static string lazyTest => __lazy_value_lazyTest ??= __lazy_init_lazyTest;
    static string __lazy_value_lazyTest2;
    [LazyProperty]
    static string __lazy_init_lazyTest2 => "str";
    public static string lazyTest2 => __lazy_value_lazyTest2 ??= __lazy_init_lazyTest2;
}