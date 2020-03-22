using System;
using Assets.Scripts;
using UnityEngine;
using GenerationAttributes;

public class TestScript : MonoBehaviour {
    void Start() {
        var test = new ToStringEnumerableTestClass();
        Debug.LogWarning(test.ToString());
        Debug.LogWarning(testExpr(2 + 4));
        var c = new ExprClass();
        c.testClassExpr();
        new ExprClass().testClassExpr();
        var x = c.testClassExpr2();
    }

    [SimpleMethodMacro(@"""${expr1} = "" + (${expr1})")]
    public static string testExpr(int value) => throw new NotImplementedException();

    public class ExprClass {
        [SimpleMethodMacro(@"Debug.LogWarning(""${expr0}"")")]
        public void testClassExpr() => throw new NotImplementedException();

        [VarMethodMacro(@"int ${varName}_backup = 10; { ${varType} ${varName} = ${varName}_backup + 2; return; }")]
        public int testClassExpr2() => throw new NotImplementedException();
    }
}
