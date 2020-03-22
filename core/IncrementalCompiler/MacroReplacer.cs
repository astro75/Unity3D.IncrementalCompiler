using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IncrementalCompiler
{
    public class MacroReplacer : CSharpSyntaxRewriter
    {
        readonly MacroProcessor.MacroCtx ctx;

        public MacroReplacer(MacroProcessor.MacroCtx ctx) {
            this.ctx = ctx;
        }

        public override SyntaxNode Visit(SyntaxNode node)
        {
            var rewritten = node;
            if (node != null)
            {
                if (ctx.changedNodes.TryGetValue(node, out var replacement))
                {
                    rewritten = replacement;
                }
                else
                {
                    rewritten = base.Visit(node);
                }
            }
            return rewritten;
        }

        public override SyntaxList<TNode> VisitList<TNode>(SyntaxList<TNode> list) {
            List<TNode> alternate = null;

            for (int i = 0, n = list.Count; i < n; i++)
            {
                var item = list[i];

                var visited = VisitListElement(item);
                var replaced = ctx.changedStatements.TryGetValue(item, out var replacementList);

                if ((item != visited || replaced) && alternate == null)
                {
                    alternate = new List<TNode>(n);
                    // not optimal
                    alternate.AddRange(list.Take(i));
                }

                if (replaced)
                {
                    foreach (var replacement in replacementList)
                    {
                        alternate.Add((TNode)(SyntaxNode) replacement);
                    }
                }

                if (alternate != null && visited != null && !visited.IsKind(SyntaxKind.None) && !replaced)
                {
                    alternate.Add(visited);
                }
            }

            return alternate != null ? SyntaxFactory.List(alternate.Select(x => x.NormalizeWhitespace())) : list;
        }
    }
}
