using System;
using Assets.Scripts;
using UnityEngine;
using GenerationAttributes;

public class TestScript : MonoBehaviour {
    void Start() {
        log(lazyTicks);
        log(ticks);

        var test = new ToStringEnumerableTestClass();
        Debug.LogWarning(test.ToString());
        Debug.LogWarning(testExpr(2 + 4));
        Debug.LogWarning(testExprWithDefault());
        var c = new ExprClass();
        c.testClassExpr();
        new ExprClass().testClassExpr();
        var x = c.testClassExpr2();
        c.statementMacro();

        nestedExpr(testExpr(20 + 20));

        Debug.LogWarning(lazyBaby);

        log(lazyTicks);
        log(ticks);
    }

    [LazyProperty] public string lazyBaby => GetType().FullName;
    [LazyProperty] public long lazyTicks => DateTime.Now.Ticks;
    public long ticks => DateTime.Now.Ticks;

    enum Enum { Val1, Val2 }

    [SimpleMethodMacro(@"""${value} = "" + (${value})")]
    static string testExpr(int value) => throw new MacroException();

    [SimpleMethodMacro(@"Debug.LogWarning(${s})")]
    static void nestedExpr(string s) => throw new MacroException();

    [SimpleMethodMacro(@"Debug.LogWarning(""${s} = "" + ${s})")]
    static void log(object s) => throw new MacroException();

    [SimpleMethodMacro(@"""${value}, ${value2}, ${value3}, ${value4}, ${value5}, ${value6}, ${value7}, ${value8}""")]
    static string testExprWithDefault(
        int value = 10, Enum value2 = Enum.Val2, ExprClass value3 = null, ExprClass value4 = default,
        int value5 = default,
        SomeStruct value6 = new SomeStruct(),
        SomeStruct value7 = default(TestScript.SomeStruct),
        SomeStruct value8 = default
    ) => throw new NotImplementedException();

    public class ExprClass {
        [SimpleMethodMacro(@"Debug.LogWarning(""${this}"")")]
        public void testClassExpr() => throw new MacroException();

        [StatementMethodMacro(@"if (true) Debug.LogWarning(""${this}"");")]
        public void statementMacro() => throw new MacroException();

        [VarMethodMacro(@"int ${varName}_backup = 10; { ${varType} ${varName} = ${varName}_backup + 2; }")]
        public int testClassExpr2() => throw new MacroException();
    }

    public struct SomeStruct {
        int a;
    }
}
