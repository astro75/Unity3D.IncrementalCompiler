using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GenerationAttributes;
using UnityEngine;

public class LinqTests {
    static int[] array = new[] {1, 2, 3, 4, 5, 6, 7};

    static readonly int staticTwo = 2;

    static void dictionary4() {
        var dict = (IDictionary<int, int>) new Dictionary<int, int>();
        var keys = dict.Select(kv => kv.Key);
    }

    static void dictionary5() {
        var dict = (IDictionary<int, int>) new Dictionary<int, int>();
        var keys = dict.Select(kv => kv.Key);
    }

    static readonly object staticInit = array.Select(_ => _ * staticTwo).ToArray();

    readonly object init = array.Select(_ => _ * staticTwo).ToArray();

    public static void aaaaaaa() {
        var arr = new[] {1, 2, 3, 4, 5, 6, 7};
        var mapped = arr.Select(_ => _ * _).ToArray();
    }

    public static void test() {
        var arr = new[] {1, 2, 3, 4, 5, 6, 7};
        var updated = arr.Where(_ => _ % 2 == 0).Select(_ => _ * _).ToArray();
        {
            var str = string.Join(", ", updated.Select(_ => _.ToString()).ToArray());
            Debug.Log(Macros.classAndMethodName+str);
        }
        {
            var array = arr.Where(_ => _ % 2 == 0).Select(_ => _ * _).ToArray();
        }
        {
            var strings = updated.Select(_ => _.ToString()).ToArray();
            var str = string.Join(", ", strings);
            Debug.Log(str);
        }
        {
            var strings =  arr.Where(_ => _ % 2 == 0).Select(_ => _ * _).Select(_ => _.ToString()).ToArray();
            var str = string.Join(", ", strings);
            Debug.Log(str);
        }
        {
            var enumerable = (IEnumerable<int>) arr;
            var updated2 = enumerable.Where(_ => _ % 2 == 0).Select(_ => _ * _).ToArray();
        }
        {
            var closure = 5;
            var mult = arr.Select(_ => _ * closure).ToArray();
        }
    }

    public static void doubleSelect() {
        var mapped = array.Select(_ => _ * _).Select(_ => _ * _).ToArray();
    }

    public static void testLocal() {
        var arr = new[] {1, 2, 3, 4, 5, 6, 7};
        int local(int x) => x * 2;
        var mapped = arr.Select(_ => local(_)).ToArray();
    }

    public static int arrowWithReturn() => array.Select(_ => _ * 2).FirstOrDefault();
    public static void voidArrow() => array.Select(_ => _ * 2).FirstOrDefault();

    public static void interesting(int num) {
        var arr = new[] {1, 2, 3, 4, 5, 6, 7};
        var mapped = arr.FirstOrDefault(_ => _ == num);
    }

    public static void interestingV2(int num) {
        {
            var num2 = num;
            var arr = new[] {1, 2, 3, 4, 5, 6, 7};
            var mapped = arr.FirstOrDefault(_ => _ == num2);
        }
    }

    public static void unsupportedLink() {
        int x = 0;
        // array.Select(_ => _ * 2).ToList().ForEach(_ => x += _);
        // array.Select(_ => _ * 2).ToList().ForEach(_ => _ += x);
        // array.Select(_ => _ * 2).ToList().ForEach(_ => _ += _);
        // array.Select(_ => _ * 2).ToList().ForEach(_ => {});

        array.Select(_ => _ * 2).FirstOrDefault(_ => _ > 5);

        var list = new List<int>() {1,2,3};
        list.ForEach(_ => x += _);
        list.ForEach(_ => _ += x);
        list.ForEach(_ => _ += _);
        list.ForEach(_ => _.Equals(_));
    }

    public static void foreachBlock() {
        var x = 0;
        var list = array.ToList();
        list.ForEach(_ => {
            x += _;
        });
    }

    public static void lambdaCapture() {
        void a(Action<int> act) {}
        void f(Func<int, int> fn) {}

        a(i => array.Select(_ => _ == i).ToArray());
        f(i => array.Select(_ => _ * i).First());
    }

    public static void nestedScopes() {
        var mapped = array.Select(_ => _ * _).ToArray();
        {
            var mapped2 = array.Select(_ => _ * _).ToArray();
        }
    }

    public static void nestedScopes2() {
        {
            var mapped2 = array.Select(_ => _ * _).ToArray();
        }
        var mapped = array.Select(_ => _ * _).ToArray();
    }

    public static void arrowWithArguments(int arg) => array.Select(_ => _ == arg).ToArray();
    public static bool arrowWithArgumentsAndReturn(int arg) => array.Select(_ => _ == arg).FirstOrDefault();

    public static void enumerableToArray() {
        var x = array.AsEnumerable();
        var y = x.Select(_ => _ * 2).ToArray();
    }

    public static void customEnum() {
        var x = new CustomEnum();
        var y = x.Select(_ => _ * 2).ToArray();
    }

    public static void passByRef() {
        int mapper(int val) => val * 2;
        var mapped = array.Select(mapper).ToArray();
    }

    public static void filter(Func<int, bool> f) {
        var filtered = array.Where(f).ToArray();
    }

    static void explicitArgumentRenameBug() {
        // var res = array.Select(_ => _.ToString()).Select(s => int.Parse(s: s)).ToArray();
    }

    static void localExpression() {
        void a(int val) => array.Select(_ => _ * val).ToArray();
    }

    static IEnumerable<int> expressionBody => array.Where(_ => _ > 5).ToArray();

    static void parameterLambda() {
        void localAct(Action<int> anything) { }
        localAct(takeMe => array.Select(_ => _ == takeMe).ToArray());

        void localFunc(Func<int, object> anything) { }
        localFunc(takeMe => array.Select(_ => _ == takeMe).ToArray());

        int localFuncRet(Func<int, object> anything) => 5;
        var five = localFuncRet(takeMe => array.Select(_ => _ == takeMe).ToArray());
        var five2 = localFuncRet(takeMe => array.Select(_ => _ == takeMe).OrderBy(_ => _));
    }

    static void lambdaTakeFromArg() {
        void localFunc(Func<int[], int, object> anything) { }
        localFunc((arr, val) => arr.Select(_ => _ == val).ToArray());
    }

    static void selectBlock() {
        var x = array.Select(_ => {
            var z = _ * _;
            return z * z;
        }).ToArray();
    }

    static void nestedFun() {
        var x = array.Select(_ => {
            var val = array.Where(_2 => _2 == _).ToArray();
            return val[0];
        }).ToArray();
    }

    static void nestedFun2() {
        var x = array.SelectMany(_ => array.Where(_2 => _2 > _).ToArray()).ToArray();
    }

    static void nestedFun3() {
        var x = array.SelectMany(_ => {
            return array.Where(_2 => _2 > _).ToArray();
        }).ToArray();
    }

    static void dictionary() {
        var dict = new Dictionary<int, int>();
        var keys = dict.Select(kv => kv.Key).ToArray();
    }

    static void dictionary2() {
        var dict = new Dictionary<int, int>();
        var keys = dict.Select(kv => kv.Key);
    }

    static void dictionary3() {
        var dict = (IDictionary<int, int>) new Dictionary<int, int>();
        var keys = dict.Select(kv => kv.Key);
    }

    static void dictionary6() {
        var dict = (IDictionary<int, int>) new Dictionary<int, int>();
        var keys = dict.Select(kv => {
            return kv.Key;
        });
    }
}

class Constructors {
    public Constructors() {
        int[] array = new[] {1, 2, 3, 4, 5, 6, 7};
        var res = array.Select(_ => _ == 3).ToArray();
    }
}

class CustomEnum : IEnumerable<int>, ICollection<int> {
    public IEnumerator<int> GetEnumerator() {
        throw new NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #region Implementation of ICollection<int>

    public void Add(int item) {
        throw new NotImplementedException();
    }

    public void Clear() {
        throw new NotImplementedException();
    }

    public bool Contains(int item) {
        throw new NotImplementedException();
    }

    public void CopyTo(int[] array, int arrayIndex) {
        throw new NotImplementedException();
    }

    public bool Remove(int item) {
        throw new NotImplementedException();
    }

    public int Count { get; }
    public bool IsReadOnly { get; }

    #endregion
}
