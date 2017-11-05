using System;
using System.Collections.Generic;
using GenerationAttributes;

namespace Assets.Scripts {
    struct DummyStruct {
        public readonly int int1, int2;
    }

    class Class { }

    enum Enum { A, B, C }
    enum ByteEnum : byte { A, B, C }
    enum LongEnum : long { A, B, C }

    [Case]
    sealed partial class ClassTest {
        public readonly int int1, int2;
        public readonly string str1, str2;
        public readonly uint uint1;
        public readonly StructTest structWithHash;
        public readonly DummyStruct structNoHash;
        public readonly float float1;
        public readonly double double1;
        public readonly long long1;
        public readonly bool bool1;
        public readonly char char1;
        public readonly byte byte1;
        public readonly sbyte sbyte1;
        public readonly short short1;
        public readonly Enum enum1;
        public readonly ByteEnum byteEnum;
        public readonly LongEnum longEnum;
        public readonly Class classRef;
    }

    [Case]
    partial struct StructTest {
        public readonly int int1, int2;
        public readonly string str1, str2;
        public readonly ClassTest classRef;
    }

    [Case]
    sealed partial class GenericClassTest<A, B, C>
        where A : struct
        where B : class 
        where C : InterfaceTest 
    {
        public readonly A valStruct;
        public readonly B valClass;
        public readonly C valInterface;
    }

    [Case]
    partial struct GenericStructTest<A> {
        public readonly A value;
    }

    partial interface InterfaceTest {}

    [Matcher]
    abstract partial class ADTBase { }

    static class ADTBaseMatcher {
        public static void voidMatch(this ADTBase self, Action<One> a0, Action<Two> a1) {
            var val0 = self as One;
            if (val0 != null) {
                a0(val0);
                return;
            }

            var val1 = self as Two;
            if (val1 != null) {
                a1(val1);
                return;
            }
        }

        public static A match<A>(this ADTBase self, Func<One, A> f0, Func<Two, A> f1) {
            var val0 = self as One;
            if (val0 != null)
                return f0(val0);
            var val1 = self as Two;
            if (val1 != null)
                return f1(val1);
            throw new ArgumentOutOfRangeException("self", self, "Should never reach this");
        }
    }

    sealed class One : ADTBase { }
    sealed class Two : ADTBase { }

    [Matcher]
    abstract partial class ADTBaseGeneric<A> { }

    sealed class GenericOne<A> : ADTBaseGeneric<A> { }
    sealed class GenericOneV2<B> : ADTBaseGeneric<B> { }
    sealed class GenericTwo : ADTBaseGeneric<string> { }
}