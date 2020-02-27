using System;
using Microsoft.CodeAnalysis;

namespace Shaman.Roslyn.LinqRewrite.DataStructures
{
    public class LiteralString : LocalizableString
    {
        private readonly string _str;
        public LiteralString(string str)
        {
            _str = str;
        }
        protected override bool AreEqual(object other)
        {
            if (other is LiteralString o) return o._str == _str;
            return false;
        }

        protected override int GetHash() => _str.GetHashCode();
        protected override string GetText(IFormatProvider formatProvider) => _str;
    }
}
