﻿using System.Collections.Generic;
using Shaman.Roslyn.LinqRewrite.DataStructures;
using Shaman.Roslyn.LinqRewrite.Services;

namespace Shaman.Roslyn.LinqRewrite
{
    public static class RewriteParametersHolder
    {
        private static readonly object Lock = new object();
        private static readonly List<RewriteParameters> Capital = new List<RewriteParameters>();

        public static RewriteParameters BorrowParameters(RewriteService rewrite, CodeCreationService code, RewriteDataService data, SyntaxInformationService info)
        {
            return new RewriteParameters(rewrite, code, data, info);
            // lock (Lock)
            // {
            //     if (Capital.Count == 0) return new RewriteParameters();
            //
            //     var count = Capital.Count;
            //     var last = Capital[count - 1];
            //     Capital.RemoveAt(count - 1);
            //     return last;
            // }
        }

        public static void ReturnParameters(RewriteParameters parameters)
        {
            // lock (Lock)
            // {
            //     Capital.Add(parameters);
            // }
        }
    }
}
