using System;
using System.Collections.Generic;
using GenerationAttributes;

namespace Assets.Scripts
{
    public partial struct CompanionasBeGenericu : System.IEquatable<CompanionasBeGenericu>
    {
        public CompanionasBeGenericu(string name, Func<int, string> get, Func<double, int> nToA)
        {
            this.name = name;
            this.get = get;
            this.nToA = nToA;
        }

        public override string ToString() => "CompanionasBeGenericu(" + "name: " + name + ", get: " + get + ", nToA: " + nToA + ")";
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

        public bool Equals(CompanionasBeGenericu other) => string.Equals(name, other.name) && get.Equals(other.get) && nToA.Equals(other.nToA);
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is CompanionasBeGenericu && Equals((CompanionasBeGenericu)obj);
        }

        public static bool operator ==(CompanionasBeGenericu left, CompanionasBeGenericu right) => left.Equals(right);
        public static bool operator !=(CompanionasBeGenericu left, CompanionasBeGenericu right) => !left.Equals(right);
        public static CompanionasBeGenericu a(string name, Func<int, string> get, Func<double, int> nToA) => new CompanionasBeGenericu(name, get, nToA);
    }
}

namespace Assets.Scripts
{
    public partial struct VienasGenericArgumentas<A> : System.IEquatable<VienasGenericArgumentas<A>>
    {
        public VienasGenericArgumentas(string name, Func<A, string> get)
        {
            this.name = name;
            this.get = get;
        }

        public override string ToString() => "VienasGenericArgumentas(" + "name: " + name + ", get: " + get + ")";
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

        public bool Equals(VienasGenericArgumentas<A> other) => string.Equals(name, other.name) && get.Equals(other.get);
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is VienasGenericArgumentas<A> && Equals((VienasGenericArgumentas<A>)obj);
        }

        public static bool operator ==(VienasGenericArgumentas<A> left, VienasGenericArgumentas<A> right) => left.Equals(right);
        public static bool operator !=(VienasGenericArgumentas<A> left, VienasGenericArgumentas<A> right) => !left.Equals(right);
    }
}

namespace Assets.Scripts
{
    public partial static class VienasGenericArgumentas
    {
        public static VienasGenericArgumentas<A> a<A>(string name, Func<A, string> get) => new VienasGenericArgumentas<A>(name, get);
    }
}

namespace Assets.Scripts
{
    public partial struct NegeneruotKonstruktoriaus<A> : System.IEquatable<NegeneruotKonstruktoriaus<A>>
    {
        public override string ToString() => "NegeneruotKonstruktoriaus(" + "name: " + name + ", get: " + get + ")";
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

        public bool Equals(NegeneruotKonstruktoriaus<A> other) => string.Equals(name, other.name) && get.Equals(other.get);
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is NegeneruotKonstruktoriaus<A> && Equals((NegeneruotKonstruktoriaus<A>)obj);
        }

        public static bool operator ==(NegeneruotKonstruktoriaus<A> left, NegeneruotKonstruktoriaus<A> right) => left.Equals(right);
        public static bool operator !=(NegeneruotKonstruktoriaus<A> left, NegeneruotKonstruktoriaus<A> right) => !left.Equals(right);
    }
}

namespace Assets.Scripts
{
    public partial struct KeliGenericArgumentai<A, N>
    {
        public KeliGenericArgumentai(string name, Func<A, string> get, Func<N, A> nToA)
        {
            this.name = name;
            this.get = get;
            this.nToA = nToA;
        }

        public override string ToString() => "KeliGenericArgumentai(" + "name: " + name + ", get: " + get + ", nToA: " + nToA + ")";
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
    public partial static class KeliGenericArgumentai
    {
        public static KeliGenericArgumentai<A, N> a<A, N>(string name, Func<A, string> get, Func<N, A> nToA) => new KeliGenericArgumentai<A, N>(name, get, nToA);
    }
}

namespace Assets.Scripts
{
    public partial struct BeCompanionAtributo : System.IEquatable<BeCompanionAtributo>
    {
        public BeCompanionAtributo(string name, Func<int, string> get, Func<double, int> nToA)
        {
            this.name = name;
            this.get = get;
            this.nToA = nToA;
        }

        public override string ToString() => "BeCompanionAtributo(" + "name: " + name + ", get: " + get + ", nToA: " + nToA + ")";
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

        public bool Equals(BeCompanionAtributo other) => string.Equals(name, other.name) && get.Equals(other.get) && nToA.Equals(other.nToA);
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is BeCompanionAtributo && Equals((BeCompanionAtributo)obj);
        }

        public static bool operator ==(BeCompanionAtributo left, BeCompanionAtributo right) => left.Equals(right);
        public static bool operator !=(BeCompanionAtributo left, BeCompanionAtributo right) => !left.Equals(right);
    }
}

namespace Assets.Scripts
{
    public partial struct JokiuMemberiu : System.IEquatable<JokiuMemberiu>
    {
        public JokiuMemberiu()
        {
        }

        public override string ToString() => "JokiuMemberiu(" + ")";
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = 0;
                return hashCode;
            }
        }

        public bool Equals(JokiuMemberiu other) => ;
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is JokiuMemberiu && Equals((JokiuMemberiu)obj);
        }

        public static bool operator ==(JokiuMemberiu left, JokiuMemberiu right) => left.Equals(right);
        public static bool operator !=(JokiuMemberiu left, JokiuMemberiu right) => !left.Equals(right);
    }
}