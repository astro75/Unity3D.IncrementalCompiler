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
}