using GenerationAttributes;

public static class MacroAssembly1
{
    [SimpleMethodMacro("\"${number}\"")]
    public static string macro1(int number) => throw new MacroException();

    public static void test() {
        var s = macro1(42);
    }
}
