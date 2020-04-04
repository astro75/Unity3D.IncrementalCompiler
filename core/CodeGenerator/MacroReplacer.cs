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
                if (ctx.ChangedNodes.TryGetValue(node, out var replacement))
                {
                    rewritten = replacement;
                }
                else
                {
                    rewritten = base.Visit(node)
                        .WithLeadingTrivia(node.GetLeadingTrivia())
                        .WithTrailingTrivia(node.GetTrailingTrivia());
                }
            }
            return rewritten;
        }

        public override SyntaxList<TNode> VisitList<TNode>(SyntaxList<TNode> list) {
            List<TNode>? alternate = null;

            for (int i = 0, n = list.Count; i < n; i++)
            {
                var item = list[i];

                var visited = VisitListElement(item);
                var replaced = ctx.ChangedStatements.TryGetValue(item, out var replacementList);

                if ((item != visited || replaced) && alternate == null)
                {
                    alternate = new List<TNode>(n);
                    // not optimal
                    alternate.AddRange(list.Take(i));
                }

                if (replaced)
                {
                    for (var index = 0; index < replacementList.Count; index++)
                    {
                        // TODO: finish whitespace logic
                        var replacement = replacementList[index].NormalizeWhitespace();
                        if (index == 0)
                            replacement = replacement.WithLeadingTrivia(item.GetLeadingTrivia());
                        if (index == replacementList.Count - 1)
                            replacement = replacement.WithTrailingTrivia(item.GetTrailingTrivia());
                        else
                            replacement = replacement.WithTrailingTrivia(SyntaxFactory.TriviaList(SyntaxFactory.LineFeed));
                        alternate!.Add((TNode) (SyntaxNode) replacement);
                    }
                }

                if (alternate != null && visited != null && !visited.IsKind(SyntaxKind.None) && !replaced)
                {
                    alternate.Add(visited);
                }
            }

            return alternate != null ? SyntaxFactory.List(alternate) : list;
        }
    }
}
