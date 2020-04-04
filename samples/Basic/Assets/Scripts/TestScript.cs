using System;
using Assets.Scripts;
using UnityEngine;
using GenerationAttributes;

public class TestScript : MonoBehaviour {
    void Start() {
        var test = new ToStringEnumerableTestClass();
        Debug.LogWarning(test.ToString());
        Debug.LogWarning(testExpr(2 + 4));
        Debug.LogWarning(testExprWithDefault());
        var c = new ExprClass();
        c.testClassExpr();
        new ExprClass().testClassExpr();
        var x = c.testClassExpr2();
        c.statementMacro();
    }

    enum Enum { Val1, Val2 }

    [SimpleMethodMacro(@"""${value} = "" + (${value})")]
    static string testExpr(int value) => throw new NotImplementedException();

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
        public void testClassExpr() => throw new NotImplementedException();

        [StatementMethodMacro(@"if (true) Debug.LogWarning(""${this}"");")]
        public void statementMacro() => throw new NotImplementedException();

        [VarMethodMacro(@"int ${varName}_backup = 10; { ${varType} ${varName} = ${varName}_backup + 2; return; }")]
        public int testClassExpr2() => throw new NotImplementedException();
    }

    public struct SomeStruct {
        int a;
    }
}
