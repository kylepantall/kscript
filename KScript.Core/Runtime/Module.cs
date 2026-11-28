using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Runtime
{
    /// <summary>Limits and defaults for a run.</summary>
    public sealed class InterpreterOptions
    {
        /// <summary>Stop with an error after this many evaluation steps (null: no limit). Servers set one so a bad loop can't hang them.</summary>
        public long? MaxSteps { get; set; }

        /// <summary>Stop with an error after this long (null: no limit).</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>How deeply functions may call each other.</summary>
        public int MaxCallDepth { get; set; } = 1000;

        /// <summary>Packages every script gets without importing them, for example from kscript.json.</summary>
        public List<string> DefaultPackages { get; } = new List<string>();

        /// <summary>
        /// Whether scripts in a project get the methods from its extension files (the extensions folder, or
        /// kscript.json's "extensions"). On by default.
        /// </summary>
        public bool ProjectExtensions { get; set; } = true;

        public CancellationToken Cancellation { get; set; }
    }

    /// <summary>
    /// Where an expression came from: its script, and the attribute or text it was written in, so errors point
    /// at the exact line and column.
    /// </summary>
    public readonly struct EvalContext
    {
        public EvalContext(Module module, DecodedText origin)
        {
            Module = module;
            Origin = origin;
        }

        public Module Module { get; }
        public DecodedText Origin { get; }

        /// <summary>The document offset of a position inside the parsed text, or null when the origin is unknown.</summary>
        public int? ToSource(int index) => Origin?.ToSource(index);
    }

    /// <summary>A loaded script: its parsed document, the packages it imports, and (once run) its variables and exports.</summary>
    public sealed class Module
    {
        internal Module(Interpreter interpreter, string path, SourceText source, MarkupDocument document)
        {
            Interpreter = interpreter;
            Path = path == null ? null : System.IO.Path.GetFullPath(path);
            Source = source;
            Document = document;
        }

        public Interpreter Interpreter { get; }

        /// <summary>The script's full path, or null for a script loaded from text.</summary>
        public string Path { get; }

        /// <summary>The folder relative paths in the script are resolved against.</summary>
        public string Directory => Path != null ? System.IO.Path.GetDirectoryName(Path) : Interpreter.Host.WorkingDirectory;

        public string DisplayName => Path != null ? System.IO.Path.GetFileName(Path) : "script";

        public SourceText Source { get; }
        public MarkupDocument Document { get; }

        /// <summary>True for a .kscript file, so messages show code in that format.</summary>
        public bool IsCode => Document?.Syntax == SourceSyntax.Code;

        /// <summary>How this file imports a package: <c>import KScript.UI</c> or <c>&lt;import package="KScript.UI" /&gt;</c>.</summary>
        public string ImportHint(string package) => IsCode ? $"import {package}" : $"<import package=\"{package}\" />";

        /// <summary>An element as this file writes it: <c>Row</c> in .kscript, <c>&lt;Row&gt;</c> in XML.</summary>
        public string ElementHint(string name) => IsCode ? name : $"<{name}>";

        /// <summary>An element's argument as this file names it in a message: <c>llm tools:</c> in .kscript, <c>&lt;llm tools&gt;</c> in XML.</summary>
        public string ArgumentHint(string element, string argument) => IsCode ? $"{element} {argument}:" : $"<{element} {argument}>";

        /// <summary>The root element: &lt;kscript&gt;, or a package element such as &lt;KScript.Window&gt;.</summary>
        public MarkupElement Root { get; internal set; }

        private List<(int Start, int End, string Key)> declarationSpans;

        /// <summary>The key of the top-level declaration holding <paramref name="offset"/> (<c>fn:greet</c>, <c>model:User</c>), or null.</summary>
        internal string DeclarationAt(int offset)
        {
            List<(int Start, int End, string Key)> spans = declarationSpans;
            if (spans == null)
            {
                spans = new List<(int, int, string)>();
                foreach (var d in Dependencies.SourceFingerprint.DeclarationsOf(Root)) spans.Add((d.Element.Span.Start, d.Element.Span.End, d.Key));
                declarationSpans = spans;
            }
            // The innermost: a method rather than the type around it.
            string found = null;
            int width = int.MaxValue;
            foreach ((int start, int end, string key) in spans)
            {
                if (offset >= start && offset < end && end - start < width)
                {
                    found = key;
                    width = end - start;
                }
            }
            return found;
        }

        /// <summary>True when the root is a package element (&lt;KScript.Window&gt;) rather than &lt;kscript&gt;.</summary>
        public bool IsPackageRoot { get; internal set; }

        /// <summary>
        /// The thing a file describes, when it describes one: the root of an XML file (<c>&lt;KScript.Routes&gt;</c>), or
        /// the one PascalCase statement at the top level of a script (<c>Routes { }</c> after its imports). Null for a
        /// plain script.
        /// </summary>
        public MarkupElement MainElement { get; internal set; }

        /// <summary>The name of <see cref="MainElement"/> without a package prefix (<c>Routes</c>, <c>Window</c>), or null.</summary>
        public string MainElementName => MainElement == null ? null : Metadata.ElementInfo.BareName(MainElement.Name);

        /// <summary>The packages this script can use, KScript.Core first.</summary>
        public IReadOnlyList<KScriptPackage> Packages => packages;

        /// <summary>The script's top-level variables, once it has started running.</summary>
        public Scope Globals { get; internal set; }

        /// <summary>What other scripts get when they import this one: top-level &lt;fn&gt; functions and &lt;let const&gt; values.</summary>
        public Dictionary<string, object> Exports { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

        internal readonly List<KScriptPackage> packages = new List<KScriptPackage>();
        internal readonly HashSet<KScriptPackage> UnqualifiedCommandPackages = new HashSet<KScriptPackage>();
        internal readonly Dictionary<string, ElementDefinition> Elements = new Dictionary<string, ElementDefinition>(StringComparer.Ordinal);
        internal readonly Dictionary<string, CommandDefinition> Commands = new Dictionary<string, CommandDefinition>(StringComparer.Ordinal);
        internal readonly Dictionary<string, MethodDefinition> Methods = new Dictionary<string, MethodDefinition>(StringComparer.Ordinal);
        /// <summary>Fields the imported packages add to values, by "receiver:name".</summary>
        internal readonly Dictionary<string, ValueFieldDefinition> Fields = new Dictionary<string, ValueFieldDefinition>(StringComparer.Ordinal);
        /// <summary>Methods this script's extend blocks add, and those of the scripts it imports, by "receiver:name".</summary>
        internal readonly Dictionary<string, ExtensionMethod> Extensions = new Dictionary<string, ExtensionMethod>(StringComparer.Ordinal);

        /// <summary>The methods the project's extension files add, shared by every script in the project (null outside one).</summary>
        internal ProjectExtensionSet ProjectExtensions;
        internal readonly Dictionary<string, VariableDefinition> Variables = new Dictionary<string, VariableDefinition>(StringComparer.Ordinal);
        internal readonly Dictionary<string, EchoFormatter> EchoFormats = new Dictionary<string, EchoFormatter>(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, ImportNamespace> Aliases = new Dictionary<string, ImportNamespace>(StringComparer.Ordinal);

        /// <summary>Each element's definition, found when the script was checked.</summary>
        internal readonly Dictionary<MarkupElement, ElementDefinition> Resolved = new Dictionary<MarkupElement, ElementDefinition>(ReferenceEqualityComparer.Instance);

        /// <summary>Scripts imported with &lt;import path&gt;, by their import element.</summary>
        internal readonly Dictionary<MarkupElement, Module> ScriptImports = new Dictionary<MarkupElement, Module>(ReferenceEqualityComparer.Instance);

        internal readonly Dictionary<object, ParseResult> ParseCache = new Dictionary<object, ParseResult>(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<object, Template> TemplateCache = new Dictionary<object, Template>(ReferenceEqualityComparer.Instance);

        /// <summary>The old spelling found in a piece of code of a .kscript file (see <see cref="KScript.Syntax.Markup.OldSpellings"/>), by the text it is in.</summary>
        internal readonly Dictionary<object, KScript.Syntax.Markup.OldSpelling> OldSpellingCache = new Dictionary<object, KScript.Syntax.Markup.OldSpelling>(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<MarkupElement, DecodedText> ContentCache = new Dictionary<MarkupElement, DecodedText>(ReferenceEqualityComparer.Instance);

        internal bool HasRun;
        internal bool Running;

        /// <summary>The package or script imported with <c>as="alias"</c>, if there is one.</summary>
        public bool TryGetAlias(string alias, out ImportNamespace import) => Aliases.TryGetValue(alias, out import);

        /// <summary>The names imported with <c>as="..."</c>.</summary>
        public IEnumerable<string> AliasNames => Aliases.Keys;

        public bool Imports(string packageName)
        {
            string name = PackageRegistry.Normalise(packageName);
            return packages.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The definition behind an element of this script, as resolved when it was loaded.</summary>
        public ElementDefinition DefinitionOf(MarkupElement element) => Resolved.TryGetValue(element, out ElementDefinition d) ? d : null;

        /// <summary>The line and column (1-based) of a document offset.</summary>
        public (int Line, int Column) PositionOf(int offset)
        {
            LinePosition p = Source.GetPosition(offset);
            return (p.Line + 1, p.Character + 1);
        }

        public override string ToString() => DisplayName;
    }
}
