using System;
using System.Collections.Generic;
using GenerationAttributes;

namespace Assets.Scripts
{
    public partial struct CCNoConstructor<A> : System.IEquatable<CCNoConstructor<A>>
    {
        public override string ToString() => "CCNoConstructor(" + "name: " + name + ", get: " + get + ")";
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = 0;
                hashCode = (hashCode * 397) ^ (name == null ? 0 : name.GetHashCode()); // System_String
                hashCode = (hashCode * 397) ^ (get == null ? 0 : get.GetHashCode()); // None
                return hashCode;
            }
        }

        public bool Equals(CCNoConstructor<A> other) => string.Equals(name, other.name) && get.Equals(other.get);
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is CCNoConstructor<A> && Equals((CCNoConstructor<A>)obj);
        }

        public static bool operator ==(CCNoConstructor<A> left, CCNoConstructor<A> right) => left.Equals(right);
        public static bool operator !=(CCNoConstructor<A> left, CCNoConstructor<A> right) => !left.Equals(right);
    }
}

namespace Assets.Scripts
{
    public partial struct CCNoStaticApply
    {
        public CCNoStaticApply(string name, Func<int, string> get, Func<double, int> nToA)
        {
            this.name = name;
            this.get = get;
            this.nToA = nToA;
        }

        public override string ToString() => "CCNoStaticApply(" + "name: " + name + ", get: " + get + ", nToA: " + nToA + ")";
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = 0;
                hashCode = (hashCode * 397) ^ (name == null ? 0 : name.GetHashCode()); // System_String
                hashCode = (hashCode * 397) ^ (get == null ? 0 : get.GetHashCode()); // None
                hashCode = (hashCode * 397) ^ (nToA == null ? 0 : nToA.GetHashCode()); // None
                return hashCode;
            }
        }
    }
}