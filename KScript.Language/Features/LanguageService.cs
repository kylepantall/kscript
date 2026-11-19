using KScript.Language.Analysis;
using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    /// <summary>
    /// Editor features over an analysed document: completion, hover, signature help, navigation, rename, outline,
    /// semantic highlighting and quick fixes. Everything works on offsets; the language server converts to LSP.
    /// </summary>
    public sealed partial class LanguageService
    {
        public LanguageService(LanguageCatalog catalog = null, IFileSystem files = null)
        {
            Catalog = catalog ?? LanguageCatalog.Default;
            Files = files ?? DiskFileSystem.Instance;
        }

        public LanguageCatalog Catalog { get; }
        public IFileSystem Files { get; }

        public DocumentAnalysis Analyze(string text, string path) => Analyzer.Analyze(text, path, Files, Catalog);

        #region Cursor context

        /// <summary>Where the cursor is, in markup and expression terms.</summary>
        internal sealed class Cursor
        {
            public int Offset;
            public MarkupElement TagElement;
            public bool InStartTag;
            public bool InElementName;
            public bool InEndTagName;
            public MarkupAttribute Attribute;
            public bool InAttributeName;
            public bool InAttributeValue;
            public ExpressionRegion Region;
            public int LocalIndex = -1;
            public bool InCode;
        }

        internal static Cursor Locate(DocumentAnalysis analysis, int offset)
        {
            Cursor c = new Cursor { Offset = offset };

            foreach (MarkupElement e in analysis.Markup.DescendantElements())
            {
                bool inStart = offset > e.StartTagSpan.Start && (offset < e.StartTagSpan.End || (!e.StartTagClosed && offset <= e.StartTagSpan.End));
                if (inStart)
                {
                    c.TagElement = e;
                    c.InStartTag = true;
                    c.InElementName = offset >= e.NameSpan.Start && offset <= e.NameSpan.End;
                    foreach (MarkupAttribute a in e.Attributes)
                    {
                        if (offset >= a.NameSpan.Start && offset <= a.NameSpan.End)
                        {
                            c.Attribute = a;
                            c.InAttributeName = true;
                        }
                        else if (a.Value != null && offset >= a.Value.SourceSpan.Start && offset <= a.Value.SourceSpan.End)
                        {
                            c.Attribute = a;
                            c.InAttributeValue = true;
                        }
                    }
                }
                else if (e.EndNameSpan.HasValue && offset >= e.EndNameSpan.Value.Start && offset <= e.EndNameSpan.Value.End)
                {
                    c.TagElement = e;
                    c.InEndTagName = true;
                }
            }

            ExpressionRegion region = analysis.RegionAt(offset);
            if (region != null && (!c.InStartTag || c.InAttributeValue))
            {
                c.Region = region;
                c.LocalIndex = region.Source.FromSource(offset);
                c.InCode = c.LocalIndex >= 0 && region.CodeSpans.Any(s => c.LocalIndex >= s.Start && c.LocalIndex <= s.End);
            }
            return c;
        }

        /// <summary>The innermost expression node at a local index in a region.</summary>
        internal static ExprNode NodeAt(ExpressionRegion region, int index) =>
            region.Nodes.SelectMany(n => n.DescendantsAndSelf())
                  .Where(n => n.Span.Start <= index && index <= n.Span.End)
                  .OrderBy(n => n.Span.Length)
                  .FirstOrDefault();

        internal TypeShape InferAt(DocumentAnalysis analysis, ExpressionRegion region, ExprNode node) =>
            new TypeInference(Catalog, analysis).Infer(node, region);

        #endregion
    }
}
