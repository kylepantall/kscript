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

        /// <summary>
        /// Code actions that fix no problem but help write the code, offered when the cursor is on their span: adding
        /// a job's <c>fn failed(err)</c>, or overriding a method a joined type gets from its bases.
        /// </summary>
        public List<CodeAction> Actions { get; } = new List<CodeAction>();
        public List<Symbol> Symbols { get; } = new List<Symbol>();
        public List<SymbolReference> References { get; } = new List<SymbolReference>();
        public List<ExpressionRegion> Regions { get; } = new List<ExpressionRegion>();
        public List<DocumentLink> Links { get; } = new List<DocumentLink>();
        public Dictionary<Symbol, ExternalLocation> ExternalDeclarations { get; } = new Dictionary<Symbol, ExternalLocation>();

        // Model options are checked before relation foreign keys and count fields are added.
        internal Dictionary<MarkupElement, FieldInfo[]> ModelFieldsAtDeclaration { get; } = new();

        /// <summary>The fields an option can name, with explicitly declared fields first.</summary>
        internal IEnumerable<FieldInfo> ModelOptionFields(MarkupElement model, string option)
        {
            if (model == null || !ModelFieldsAtDeclaration.TryGetValue(model, out FieldInfo[] fields))
            {
                return Array.Empty<FieldInfo>();
            }
            // An explicit key must be declared, even when the model would otherwise get an automatic id.
            return option == "key" ? fields.Where(f => f.Declaration.HasValue) : fields;
        }

        /// <summary>Where the members of an <c>&lt;import as="..."&gt;</c> alias are declared in the imported file: lib.slugify.</summary>
        public Dictionary<(Symbol Alias, string Member), ExternalLocation> ExternalMembers { get; } = new Dictionary<(Symbol, string), ExternalLocation>();
        public Scope RootScope { get; internal set; }

        /// <summary>The inferred type for each bare pattern literal, by source offset.</summary>
        internal Dictionary<int, Symbol> PatternLiteralTypes { get; } = new Dictionary<int, Symbol>();

        /// <summary>Words joined by - + / or . that a pattern type reads (my-post, 1.4.2), by the word's span.</summary>
        internal Dictionary<TextSpan, Symbol> PatternLiteralWords { get; } = new Dictionary<TextSpan, Symbol>();

        /// <summary>
        /// The methods extend blocks add, by receiver type then name: this file's, its imports' and the project's
        /// extension files'. External ones have a <see cref="Symbol.SourcePath"/> and an entry in ExternalDeclarations.
        /// </summary>
        public Dictionary<string, Dictionary<string, Symbol>> ExtensionMethods { get; } = new Dictionary<string, Dictionary<string, Symbol>>(StringComparer.Ordinal);

        /// <summary>The receivers whose extension methods a value of this shape has, most specific first.</summary>
        private static IEnumerable<string> ExtensionReceivers(TypeShape receiver)
        {
            if (receiver == null)
            {
                yield break;
            }
            if (receiver.Name != null && receiver.Type == KType.Map)
            {
                yield return receiver.Name;
            }
            yield return receiver.Type;
        }

        /// <summary>The extension method <c>value->name()</c> calls, or null.</summary>
        public Symbol ExtensionMethod(TypeShape receiver, string name)
        {
            foreach (string r in ExtensionReceivers(receiver))
            {
                if (ExtensionMethods.TryGetValue(r, out Dictionary<string, Symbol> methods) && methods.TryGetValue(name, out Symbol method))
                {
                    return method;
                }
            }
            return null;
        }

        /// <summary>Every extension method a value of this shape has.</summary>
        public IEnumerable<Symbol> ExtensionMethodsFor(TypeShape receiver) =>
            ExtensionReceivers(receiver).SelectMany(r => ExtensionMethods.TryGetValue(r, out Dictionary<string, Symbol> m) ? m.Values : Enumerable.Empty<Symbol>())
                .GroupBy(m => m.Name, StringComparer.Ordinal).Select(g => g.First());

        /// <summary>
        /// The names this document uses from the scripts it imports, as (full path of the script, name): a call to an
        /// imported function, or <c>lib.slugify</c> through an alias. It is how a script's functions count as used
        /// when only another file calls them.
        /// </summary>
        public HashSet<(string Path, string Name)> ExternalUses { get; } = new HashSet<(string, string)>();

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

    /// <summary>A fix offered on a span without a diagnostic, like "Override fn hi() from User".</summary>
    public sealed class CodeAction
    {
        public CodeAction(TextSpan span, QuickFix fix)
        {
            Span = span;
            Fix = fix;
        }

        public TextSpan Span { get; }
        public QuickFix Fix { get; }
    }
}
