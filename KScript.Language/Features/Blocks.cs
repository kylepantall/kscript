using KScript.Language.Analysis;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    /// <summary>One top-level block of a script, shown as a notebook-style cell with a divider line above it.</summary>
    public sealed class CodeBlock
    {
        public CodeBlock(TextSpan span, string label)
        {
            Span = span;
            Label = label;
        }

        public TextSpan Span { get; internal set; }

        /// <summary>A short name: the comment that heads the block, or what it declares (<c>fn greet</c>).</summary>
        public string Label { get; internal set; }

        public override string ToString() => Label;
    }

    /// <summary>A region the editor can fold: any element or comment that covers more than one line.</summary>
    public sealed class FoldingRegion
    {
        public FoldingRegion(int startLine, int endLine, bool isComment)
        {
            StartLine = startLine;
            EndLine = endLine;
            IsComment = isComment;
        }

        public int StartLine { get; }
        public int EndLine { get; }
        public bool IsComment { get; }
    }

    public sealed partial class LanguageService
    {
        /// <summary>Elements that continue the block before them instead of starting one.</summary>
        private static readonly HashSet<string> ContinuationElements = new HashSet<string>(StringComparer.Ordinal) { "elif", "else", "catch", "finally" };

        /// <summary>
        /// Splits a script's top level into blocks, like notebook cells. When the script has top-level comments,
        /// each comment starts a block that runs to the next one (the comments read as section headings). Otherwise
        /// an element that spans several lines (a function, loop or if, with its elif and else) is a block of its own,
        /// and a run of one-line elements (a few &lt;let&gt;s, say) shares one. Pages get none: their layout is HTML.
        /// </summary>
        public List<CodeBlock> GetBlocks(DocumentAnalysis analysis)
        {
            List<CodeBlock> blocks = new List<CodeBlock>();
            MarkupElement root = analysis.Kind == DocumentKind.Script ? analysis.Markup.RootElements.FirstOrDefault() : null;
            if (root == null)
            {
                return blocks;
            }

            SourceText source = analysis.Source;
            List<MarkupNode> nodes = root.Children.Where(n => n is MarkupComment || n is MarkupElement).ToList();
            bool sections = nodes.OfType<MarkupComment>().Any();
            CodeBlock current = null;
            bool previousWasComment = false;
            bool acceptsOneLiners = false;

            foreach (MarkupNode node in nodes)
            {
                if (node is MarkupComment comment)
                {
                    // Two comments in a row (a heading, then a note) stay in one block.
                    if (current != null && previousWasComment)
                    {
                        current.Span = TextSpan.FromBounds(current.Span.Start, comment.Span.End);
                    }
                    else
                    {
                        current = new CodeBlock(comment.Span, CommentLabel(source.Text, comment.Span));
                        blocks.Add(current);
                    }
                    previousWasComment = true;
                    acceptsOneLiners = true;
                    continue;
                }

                MarkupElement e = (MarkupElement)node;
                bool multiLine = source.GetPosition(e.Span.Start).Line != source.GetPosition(e.Span.End).Line;
                bool join = current != null && (sections || previousWasComment || ContinuationElements.Contains(e.Name) || (acceptsOneLiners && !multiLine));
                if (join)
                {
                    current.Span = TextSpan.FromBounds(current.Span.Start, e.Span.End);
                }
                else
                {
                    current = new CodeBlock(e.Span, ElementLabel(e));
                    blocks.Add(current);
                }
                previousWasComment = false;
                // After a function or loop, the next one-line element starts a new block.
                acceptsOneLiners = !multiLine;
            }
            return blocks;
        }

        /// <summary>Folding for every element and comment that covers more than one line.</summary>
        public List<FoldingRegion> GetFoldingRegions(DocumentAnalysis analysis)
        {
            List<FoldingRegion> regions = new List<FoldingRegion>();
            SourceText source = analysis.Source;

            void Visit(IEnumerable<MarkupNode> nodes)
            {
                foreach (MarkupNode node in nodes)
                {
                    int start = source.GetPosition(node.Span.Start).Line;
                    int end = source.GetPosition(Math.Max(node.Span.Start, node.Span.End - 1)).Line;
                    if (node is MarkupComment && end > start)
                    {
                        regions.Add(new FoldingRegion(start, end, isComment: true));
                    }
                    else if (node is MarkupElement e)
                    {
                        // Fold up to the line before </tag>, so the closing tag stays visible.
                        if (end - 1 > start)
                        {
                            regions.Add(new FoldingRegion(start, end - 1, isComment: false));
                        }
                        Visit(e.Children);
                    }
                }
            }

            Visit(analysis.Markup.Children);
            return regions;
        }

        private static string CommentLabel(string text, TextSpan span)
        {
            string inner = text.Substring(span.Start, span.Length);
            inner = inner.StartsWith("<!--", StringComparison.Ordinal) ? inner.Substring(4) : inner;
            inner = inner.EndsWith("-->", StringComparison.Ordinal) ? inner.Substring(0, inner.Length - 3) : inner;
            string first = inner.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            return first.Length > 60 ? first.Substring(0, 57) + "..." : first;
        }

        private static string ElementLabel(MarkupElement e)
        {
            string name = e.GetAttributeValue("name")?.Trim();
            return string.IsNullOrEmpty(name) ? e.Name : e.Name + " " + name;
        }
    }
}
