using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>A link from the document to another file: a component, layout or import.</summary>
    public sealed class DocumentLink
    {
        public DocumentLink(TextSpan span, string targetPath, string kind)
        {
            Span = span;
            TargetPath = targetPath;
            Kind = kind;
        }

        public TextSpan Span { get; }
        public string TargetPath { get; }

        /// <summary>component, layout or import.</summary>
        public string Kind { get; }
    }

    /// <summary>A declaration in another file (an imported function).</summary>
    public sealed class ExternalLocation
    {
        public ExternalLocation(string path, LinePosition start, LinePosition end)
        {
            Path = path;
            Start = start;
            End = end;
        }

        public string Path { get; }
        public LinePosition Start { get; }
        public LinePosition End { get; }
    }

    /// <summary>Everything the analyzer learned about one document.</summary>
    public sealed class DocumentAnalysis
    {
        internal DocumentAnalysis(SourceText source, string path, DocumentKind kind, MarkupDocument markup)
        {
            Source = source;
            Path = path;
            Kind = kind;
            Markup = markup;
        }

        public SourceText Source { get; }

        /// <summary>Reads a JSON file named in the document (e.g. by @read_json) and returns its shape, or null.</summary>
        internal Func<string, TypeShape> JsonFileShape { get; set; }

        /// <summary>The file path, or null for unsaved text.</summary>
        public string Path { get; }

        public DocumentKind Kind { get; }
        public MarkupDocument Markup { get; }
        public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();
        public List<Symbol> Symbols { get; } = new List<Symbol>();
        public List<SymbolReference> References { get; } = new List<SymbolReference>();
        public List<ExpressionRegion> Regions { get; } = new List<ExpressionRegion>();
        public List<DocumentLink> Links { get; } = new List<DocumentLink>();
        public Dictionary<Symbol, ExternalLocation> ExternalDeclarations { get; } = new Dictionary<Symbol, ExternalLocation>();
        public Scope RootScope { get; internal set; }

        /// <summary>The packages a script imports (always KScript.Core). Empty for pages.</summary>
        public IReadOnlyList<string> ImportedPackages { get; internal set; } = new[] { "KScript.Core" };

        /// <summary>True for layouts and components, whose variables are supplied by the page that uses them.</summary>
        public bool IsPartial { get; internal set; }

        /// <summary>
        /// For a layout or component: the variables it uses without declaring, so the page must pass them
        /// (a component's props), with where each is first used. In order of first use.
        /// </summary>
        public Dictionary<string, TextSpan> Inputs { get; } = new Dictionary<string, TextSpan>(StringComparer.Ordinal);

        /// <summary>The innermost expression region containing the offset.</summary>
        public ExpressionRegion RegionAt(int offset) =>
            Regions.Where(r => r.SourceSpan.Contains(offset)).OrderBy(r => r.SourceSpan.Length).FirstOrDefault();

        /// <summary>The symbol reference (or declaration) under the offset.</summary>
        public SymbolReference ReferenceAt(int offset) =>
            References.Where(r => r.Span.Contains(offset)).OrderBy(r => r.Span.Length).FirstOrDefault();

        public IEnumerable<SymbolReference> ReferencesTo(Symbol symbol) => References.Where(r => r.Symbol == symbol);

        /// <summary>The scope at an offset.</summary>
        public Scope ScopeAt(int offset) => RootScope.Innermost(offset);

        public DocumentLink LinkAt(int offset) => Links.FirstOrDefault(l => l.Span.Contains(offset));

        public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
    }
}
