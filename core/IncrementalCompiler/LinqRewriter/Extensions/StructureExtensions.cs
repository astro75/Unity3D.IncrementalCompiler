using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Shaman.Roslyn.LinqRewrite.Extensions
{
    public static class StructureExtensions
    {
        private static readonly Dictionary<ITypeSymbol, int> StructSizeCache = new Dictionary<ITypeSymbol, int>();

        public static int GetStructSize(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: return 4;
                case SpecialType.System_Char: return 2;
                case SpecialType.System_SByte: return 1;
                case SpecialType.System_Byte: return 1;
                case SpecialType.System_Int16: return 2;
                case SpecialType.System_UInt16: return 2;
                case SpecialType.System_Int32: return 4;
                case SpecialType.System_UInt32: return 4;
                case SpecialType.System_Int64: return 8;
                case SpecialType.System_UInt64: return 8;
                case SpecialType.System_Single: return 4;
                case SpecialType.System_Double: return 8;
                case SpecialType.System_IntPtr: return 8;
                case SpecialType.System_UIntPtr: return 8;
            }
            if (StructSizeCache.TryGetValue(type, out var size)) return size;
            
            size = 0;
            foreach (var item in type.GetMembers())
            {
                if (item.Kind != SymbolKind.Field || item.IsStatic) continue;

                var field = (IFieldSymbol) item;
                if (field.Type.IsValueType)
                {
                    if (field.Type.Equals(type))
                    {
                        // This is a primitive-like type, it "contains" itself.
                        // An unknown one, since we already ruled out some well know ones above.
                        size = 8;
                        break;
                    }

                    size += GetStructSize(field.Type);
                }
                else size += 64 / 8;
            }

            StructSizeCache[type] = size;
            return size;
        }
    }
}