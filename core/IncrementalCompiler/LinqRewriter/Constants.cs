using System.Collections.Generic;

namespace Shaman.Roslyn.LinqRewrite
{
    public static class Constants
    {
        //public const string ToDictionaryWithKeyMethod = "System.Collections.Generic.IEnumerable<TSource>.ToDictionary<TSource, TKey>(System.Func<TSource, TKey>)";
        public const string ToDictionaryWithKeyValueMethod = "System.Collections.Generic.IEnumerable<TSource>.ToDictionary<TSource, TKey, TElement>(System.Func<TSource, TKey>, System.Func<TSource, TElement>)";
        public const string ToArrayMethod = "System.Collections.Generic.IEnumerable<TSource>.ToArray<TSource>()";
        public const string ToListMethod = "System.Collections.Generic.IEnumerable<TSource>.ToList<TSource>()";
        public const string ReverseMethod = "System.Collections.Generic.IEnumerable<TSource>.Reverse<TSource>()";
        public const string FirstMethod = "System.Collections.Generic.IEnumerable<TSource>.First<TSource>()";
        public const string SingleMethod = "System.Collections.Generic.IEnumerable<TSource>.Single<TSource>()";
        public const string LastMethod = "System.Collections.Generic.IEnumerable<TSource>.Last<TSource>()";
        public const string FirstOrDefaultMethod = "System.Collections.Generic.IEnumerable<TSource>.FirstOrDefault<TSource>()";
        public const string SingleOrDefaultMethod = "System.Collections.Generic.IEnumerable<TSource>.SingleOrDefault<TSource>()";
        public const string LastOrDefaultMethod = "System.Collections.Generic.IEnumerable<TSource>.LastOrDefault<TSource>()";
        public const string FirstWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.First<TSource>(System.Func<TSource, bool>)";
        public const string SingleWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Single<TSource>(System.Func<TSource, bool>)";
        public const string LastWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Last<TSource>(System.Func<TSource, bool>)";
        public const string FirstOrDefaultWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.FirstOrDefault<TSource>(System.Func<TSource, bool>)";
        public const string SingleOrDefaultWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.SingleOrDefault<TSource>(System.Func<TSource, bool>)";
        public const string LastOrDefaultWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.LastOrDefault<TSource>(System.Func<TSource, bool>)";

        public const string CountMethod = "System.Collections.Generic.IEnumerable<TSource>.Count<TSource>()";
        public const string CountWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Count<TSource>(System.Func<TSource, bool>)";
        public const string LongCountMethod = "System.Collections.Generic.IEnumerable<TSource>.LongCount<TSource>()";
        public const string LongCountWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.LongCount<TSource>(System.Func<TSource, bool>)";

        public const string ElementAtMethod = "System.Collections.Generic.IEnumerable<TSource>.ElementAt<TSource>(int)";
        public const string ElementAtOrDefaultMethod = "System.Collections.Generic.IEnumerable<TSource>.ElementAtOrDefault<TSource>(int)";

        public const string AnyMethod = "System.Collections.Generic.IEnumerable<TSource>.Any<TSource>()";
        public const string AnyWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.Any<TSource>(System.Func<TSource, bool>)";

        public const string AllWithConditionMethod = "System.Collections.Generic.IEnumerable<TSource>.All<TSource>(System.Func<TSource, bool>)";

        public const string ContainsMethod = "System.Collections.Generic.IEnumerable<TSource>.Contains<TSource>(TSource)";

        public const string ListForEachMethod = "System.Collections.Generic.List<T>.ForEach(System.Action<T>)";
        public const string EnumerableForEachMethod = "System.Collections.Generic.IEnumerable<T>.ForEach<T>(System.Action<T>)";

        //readonly static string RecursiveEnumerationMethod = "T.RecursiveEnumeration<T>(System.Func<T, T>)";

        public const string WhereMethod = "System.Collections.Generic.IEnumerable<TSource>.Where<TSource>(System.Func<TSource, bool>)";
        public const string SelectMethod = "System.Collections.Generic.IEnumerable<TSource>.Select<TSource, TResult>(System.Func<TSource, TResult>)";
        public const string CastMethod = "System.Collections.IEnumerable.Cast<TResult>()";
        public const string OfTypeMethod = "System.Collections.IEnumerable.OfType<TResult>()";

        public static readonly HashSet<string> KnownMethods = new HashSet<string>
        {
            ToDictionaryWithKeyValueMethod, ToArrayMethod, ToListMethod, ReverseMethod, 
            
            FirstMethod, SingleMethod, LastMethod,
            FirstOrDefaultMethod, SingleOrDefaultMethod, LastOrDefaultMethod,
            FirstWithConditionMethod, SingleWithConditionMethod, LastWithConditionMethod,
            FirstOrDefaultWithConditionMethod, SingleOrDefaultWithConditionMethod, LastOrDefaultWithConditionMethod,
            
            CountMethod, CountWithConditionMethod, LongCountMethod, LongCountWithConditionMethod,
            
            ElementAtMethod, ElementAtOrDefaultMethod,
            
            AnyMethod, AnyWithConditionMethod, AllWithConditionMethod,
            
            ContainsMethod,
            
            ListForEachMethod,
            
            EnumerableForEachMethod
        };

        public static readonly string[] RootMethodsThatRequireYieldReturn = {
            WhereMethod, SelectMethod, CastMethod, OfTypeMethod
        };

        public static readonly string[] MethodsThatPreserveCount = {
            SelectMethod, CastMethod, ReverseMethod, ToListMethod, ToArrayMethod /*OrderBy*/
        };

        public const string ItemsName = "_linqitems";
        public const string ItemName = "_linqitem";

        public const int MaximumSizeForByValStruct = 128 / 8; // eg. two longs, or two references
    }
}