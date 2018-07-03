using System.Collections.Generic;
using GenerationAttributes;

namespace Lol {
    public class Lol { }
}

namespace Assets.Scripts.Lol {
    public class Lol { }
}

namespace Assets.Scripts {
    public class XD { }

    public partial class PublicAccessorTest {
        [PublicAccessor] int _num;
        [PublicAccessor] XD _xd;
        [PublicAccessor] global::Lol.Lol _globalLol;
        [PublicAccessor] Lol.Lol _lol;
        [PublicAccessor] int[] numArray;
        [PublicAccessor] List<Lol.Lol> lolList;
    }
}
