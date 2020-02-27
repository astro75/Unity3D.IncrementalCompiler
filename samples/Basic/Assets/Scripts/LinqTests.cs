using System.Collections.Generic;
using System.Linq;
using GenerationAttributes;
using UnityEngine;

public class LinqTests {
    public static void test() {
        var arr = new[] {1, 2, 3, 4, 5, 6, 7};
        var updated = arr.Where(_ => _ % 2 == 0).Select(_ => _ * _);
        {
            var str = string.Join(", ", updated.Select(_ => _.ToString()));
            Debug.Log(str);
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
}
