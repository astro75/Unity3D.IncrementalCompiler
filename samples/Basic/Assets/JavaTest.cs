using GenerationAttributes;

namespace DefaultNamespace {
    [JavaClass(
        module: "plugin",
        classBody: "// wheeeee"
    )]
    public static partial class JavaTest {
        [JavaMethod("return b;")]
        public static int test(string a, int b) => default;

        [JavaMethod("return a + b;")]
        public static string test2(string a, int b) => default;

        //[JavaMethod("return n.ToString();")]
        public static string nullable(int? n) => default;

//        [JavaMethod("return;")]
        public static void testVoid(string a, int b) { }
    }
}
