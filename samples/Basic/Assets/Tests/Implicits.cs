using System.Collections.Generic;
using GenerationAttributes;
using JetBrains.Annotations;

namespace Assets.Scripts {
    public class ImplicitsBase {
        // [Implicit] protected float floatMember = 5;
    }

    public class Implicits : ImplicitsBase {
        // [Implicit] static int staticMember = 0;
        // [Implicit] int privateMember = 0;
        // [Implicit] float floatMember = 0;
        // [Implicit] float floatMember1 = 0;

        [Implicit] IList<int> listInstance = default;

        [ImplicitPassThrough] void iTestStuff([Implicit] int iAmAlsoImplicit = default) {
          // float floatMember = 10;
          testMe();
          void hiddenImplicit(string iAmAlsoImplicitxx) {
            testMe();
          }
        }

        [ImplicitPassThrough] void testMe() {
          // var floatMember = 0f;
          testMe2();
          testMe3();
        }

        [ImplicitPassThrough] static void testMe3(
          [Implicit] IList<IList<IList<int>>> list = default,
          [Implicit] IList<int> list1 = default,
          [Implicit] int aaa = default
        ) {
          // iTestStuff();
          testMe2();
        }

        static void testMe2([Implicit] int iAmImplicit = default, [Implicit] float fff = default) {

        }
    }
}
