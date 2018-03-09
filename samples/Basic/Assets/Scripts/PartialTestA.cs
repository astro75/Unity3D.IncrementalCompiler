using GenerationAttributes;

namespace Assets.Scripts.Partial {
    [Record]
    sealed partial class DoubleFilePartial {
        [PublicAccessor] readonly string _val1;
        [PublicAccessor] readonly string _val2;
        [PublicAccessor] readonly string _val3;
    }
}
