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

        /// <summary>The root element: &lt;kscript&gt;, or a package element such as &lt;KScript.Window&gt;.</summary>
        public MarkupElement Root { get; internal set; }

        /// <summary>True when the root is a package element (&lt;KScript.Window&gt;) rather than &lt;kscript&gt;.</summary>
        public bool IsPackageRoot { get; internal set; }

        /// <summary>The packages this script can use, KScript.Core first.</summary>
        public IReadOnlyList<KScriptPackage> Packages => packages;

        /// <summary>The script's top-level variables, once it has started running.</summary>
        public Scope Globals { get; internal set; }

        /// <summary>What other scripts get when they import this one: top-level &lt;fn&gt; functions and &lt;let const&gt; values.</summary>
        public Dictionary<string, object> Exports { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

        internal readonly List<KScriptPackage> packages = new List<KScriptPackage>();
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

        /// <summary>Scripts imported with &lt;import src&gt;, by their import element.</summary>
        internal readonly Dictionary<MarkupElement, Module> ScriptImports = new Dictionary<MarkupElement, Module>(ReferenceEqualityComparer.Instance);

        internal readonly Dictionary<object, ParseResult> ParseCache = new Dictionary<object, ParseResult>(ReferenceEqualityComparer.Instance);
        internal readonly Dictionary<object, Template> TemplateCache = new Dictionary<object, Template>(ReferenceEqualityComparer.Instance);
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
