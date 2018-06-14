using System;
using System.Collections.Generic;
using GenerationAttributes;

namespace Assets.Scripts
{
    public partial struct CompanionNoGenerics : System.IEquatable<CompanionNoGenerics>
    {
        public CompanionNoGenerics(string name, Func<int, string> get, Func<double, int> nToA)
        {
            this.name = name;
            this.get = get;
            this.nToA = nToA;
        }

        public override string ToString() => "CompanionNoGenerics(" + "name: " + name + ", get: " + get + ", nToA: " + nToA + ")";
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

        public bool Equals(CompanionNoGenerics other) => string.Equals(name, other.name) && get.Equals(other.get) && nToA.Equals(other.nToA);
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is CompanionNoGenerics && Equals((CompanionNoGenerics)obj);
        }

        public static bool operator ==(CompanionNoGenerics left, CompanionNoGenerics right) => left.Equals(right);
        public static bool operator !=(CompanionNoGenerics left, CompanionNoGenerics right) => !left.Equals(right);
    }
}

namespace Assets.Scripts
{
    public partial struct OneGenericArg<A> : System.IEquatable<OneGenericArg<A>>
    {
        public OneGenericArg(string name, Func<A, string> get)
        {
            this.name = name;
            this.get = get;
        }

        public override string ToString() => "OneGenericArg(" + "name: " + name + ", get: " + get + ")";
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

        public bool Equals(OneGenericArg<A> other) => string.Equals(name, other.name) && get.Equals(other.get);
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is OneGenericArg<A> && Equals((OneGenericArg<A>)obj);
        }

        public static bool operator ==(OneGenericArg<A> left, OneGenericArg<A> right) => left.Equals(right);
        public static bool operator !=(OneGenericArg<A> left, OneGenericArg<A> right) => !left.Equals(right);
    }
}

namespace Assets.Scripts
{
    public partial struct NoConstructor<A> : System.IEquatable<NoConstructor<A>>
    {
        public override string ToString() => "NoConstructor(" + "name: " + name + ", get: " + get + ")";
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

        public bool Equals(NoConstructor<A> other) => string.Equals(name, other.name) && get.Equals(other.get);
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is NoConstructor<A> && Equals((NoConstructor<A>)obj);
        }

        public static bool operator ==(NoConstructor<A> left, NoConstructor<A> right) => left.Equals(right);
        public static bool operator !=(NoConstructor<A> left, NoConstructor<A> right) => !left.Equals(right);
    }
}

namespace Assets.Scripts
{
    public partial struct SeveralGenericArgs<A, N>
    {
        public SeveralGenericArgs(string name, Func<A, string> get, Func<N, A> nToA)
        {
            this.name = name;
            this.get = get;
            this.nToA = nToA;
        }

        public override string ToString() => "SeveralGenericArgs(" + "name: " + name + ", get: " + get + ", nToA: " + nToA + ")";
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

namespace Assets.Scripts
{
    public partial struct NoCompanionAttribute : System.IEquatable<NoCompanionAttribute>
    {
        public NoCompanionAttribute(string name, Func<int, string> get, Func<double, int> nToA)
        {
            this.name = name;
            this.get = get;
            this.nToA = nToA;
        }

        public override string ToString() => "NoCompanionAttribute(" + "name: " + name + ", get: " + get + ", nToA: " + nToA + ")";
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

        public bool Equals(NoCompanionAttribute other) => string.Equals(name, other.name) && get.Equals(other.get) && nToA.Equals(other.nToA);
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is NoCompanionAttribute && Equals((NoCompanionAttribute)obj);
        }

        public static bool operator ==(NoCompanionAttribute left, NoCompanionAttribute right) => left.Equals(right);
        public static bool operator !=(NoCompanionAttribute left, NoCompanionAttribute right) => !left.Equals(right);
    }
}