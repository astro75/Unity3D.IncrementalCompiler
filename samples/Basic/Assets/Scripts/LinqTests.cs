using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GenerationAttributes;
using UnityEngine;

public class LinqTests {
    public static void aaaaaaa() {
        var arr = new[] {1, 2, 3, 4, 5, 6, 7};
        var mapped = arr.Select(_ => _ * _).ToArray();
    }

    public static void test() {
        var arr = new[] {1, 2, 3, 4, 5, 6, 7};
        var updated = arr.Where(_ => _ % 2 == 0).Select(_ => _ * _);
        {
            var str = string.Join(", ", updated.Select(_ => _.ToString()));
            Debug.Log(Macros.classAndMethodName+str);
        }
        {
            var array = arr.Where(_ => _ % 2 == 0).Select(_ => _ * _).ToArray();
        }
        {
            var strings = updated.Select(_ => _.ToString());
            var str = string.Join(", ", strings);
            Debug.Log(str);
        }
        {
            var strings =  arr.Where(_ => _ % 2 == 0).Select(_ => _ * _).Select(_ => _.ToString());
            var str = string.Join(", ", strings);
            Debug.Log(str);
        }
        {
            var enumerable = (IEnumerable<int>) arr;
            var updated2 = enumerable.Where(_ => _ % 2 == 0).Select(_ => _ * _);
        }
        {
            var closure = 5;
            var mult = arr.Select(_ => _ * closure);
        }
    }

    public static void testLocal() {
        var arr = new[] {1, 2, 3, 4, 5, 6, 7};
        int local(int x) => x * 2;
        var mapped = arr.Select(_ => local(_));
    }

    static int[] array = new[] {1, 2, 3, 4, 5, 6, 7};

    public static int a() => array.Select(_ => _ * 2).FirstOrDefault();

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
        array.Select(_ => _ * 2).ToList().ForEach(_ => x += _);
        array.Select(_ => _ * 2).ToList().ForEach(_ => _ += x);
        array.Select(_ => _ * 2).ToList().ForEach(_ => _ += _);
        array.Select(_ => _ * 2).ToList().ForEach(_ => {});

        var list = new List<int>() {1,2,3};
        list.ForEach(_ => x += _);
        list.ForEach(_ => _ += x);
        list.ForEach(_ => _ += _);
        list.ForEach(_ => _.Equals(_));

        list.ForEach(_ => {
            x += _;
        });
    }

    public static void lambdaCapture() {
        void a(Action<int> act) {}
        void f(Func<int, int> fn) {}

        a(i => array.Select(_ => _ == i));
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

    public static void enumerableToArray() {
        var x = array.AsEnumerable();
        var y = x.Select(_ => _ * 2).ToArray();
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

    public static void customEnum() {
        var x = new CustomEnum();
        var y = x.Select(_ => _ * 2).ToArray();
    }
}
