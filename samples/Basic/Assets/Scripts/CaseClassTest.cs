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
    abstract partial class ADTBase {
        static void sample() {
            var val = (ADTBase) new One(1);
            val.match(
                one => one.val,
                two => 2
            );
        }
    }

    [Case]
    sealed partial class One : ADTBase {
        public readonly int val;
    }

    [Case]
    sealed partial class Two : ADTBase {
        public readonly string val;
    }

    [Matcher]
    abstract partial class ADTBaseGeneric<A> { }

    sealed class GenericOne<A> : ADTBaseGeneric<A> { }
    sealed class GenericOneV2<B> : ADTBaseGeneric<B> { }
    sealed class GenericTwo : ADTBaseGeneric<string> { }
}