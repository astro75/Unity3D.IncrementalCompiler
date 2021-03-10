using System;
using System.Collections.Generic;
using GenerationAttributes;

namespace Tests2 {
  public partial class AttrMacroSample {
    [AttributeMacro("public int x; " +
                    "public ${memberType} ${_memberName} = ${x};" +
                    "public ${className} instance = new ${className}();")]
    public class SomeAttr : Attribute {
      public int x;
    }

    [AttributeMacro("public ${memberType} ${_memberName} => ${memberName};")]
    public class PublicAccessorAttr : Attribute { }

    [AttributeMacro("public IList<${memberType#0}> ${_memberName} => ${memberName};")]
    public class ReadOnlyListAttr : Attribute { }

    // this approach may not be valid: it can't handle initial state
    [AttributeMacro("private ${memberType} _gen_${memberName};" +
                    "private ${memberType} ${memberName}2 { get => _gen_${memberName}; set { " +
                    "   if (_gen_${memberName} != value) {" +
                    "     _gen_${memberName} = value;" +
                    "     ${onChange}(value);" +
                    "   }" +
                    " } }")]
    public class Rx : Attribute {
      public string onChange;
    }

    public partial class MyClass {
      [SomeAttr(x = 10)] public int aa;
      [PublicAccessorAttr] string _myField = "aaa";
      [PublicAccessorAttr] List<int> _myList;
      [ReadOnlyListAttr] List<int> _myList2;

      [Rx(onChange = nameof(onRxChange))] int rxTest;
      void onRxChange(int val) { }
    }
  }
}
