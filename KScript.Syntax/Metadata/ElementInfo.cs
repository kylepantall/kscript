namespace KScript.Metadata
{
    /// <summary>How a declaration option refers to the fields of its own model.</summary>
    public enum ModelFieldKind { None, Single, List, Groups }

    public sealed class AttributeInfo
    {
        public AttributeInfo(string name, AttributeKind kind, string description, bool required = false, params string[] options)
        {
            Name = name;
            Kind = kind;
            Description = description;
            Required = required;
            Options = options ?? Array.Empty<string>();
        }

        public string Name { get; }
        public AttributeKind Kind { get; }
        public string Description { get; }
        public bool Required { get; }
        public IReadOnlyList<string> Options { get; }

        /// <summary>
        /// The type of value an expression attribute takes, when it has one: <c>duration</c> for a length of time
        /// (<c>timeout: 30s</c>), or <c>slash_path</c> for a text argument that is a path (<c>path: /people/{id}</c>),
        /// which may be written with or without quotes. Null when any value fits.
        /// </summary>
        public string Type { get; init; }

        /// <summary>The fixed arguments passed to a function reference, or null for an unrestricted reference.</summary>
        public Signature FunctionSignature { get; init; }
        public bool RequiresDeclaredRecord { get; init; }

        public ModelFieldKind ModelFields { get; init; }

        /// <summary>Another attribute that can stand in for this one when it is required (<c>value</c> or element text).</summary>
        public bool SatisfiedByContent { get; init; }

        /// <summary>True for text that is used as written, without <c>{ }</c> interpolation, such as a route path <c>/people/{id}</c>.</summary>
        public bool Literal { get; init; }

        /// <summary>True for a bare word that names something in the project (a controller, a queue), so it is written without quotes like any other name.</summary>
        public bool IsName { get; init; }

        /// <summary>Set when the attribute is deprecated; it still works, with a warning.</summary>
        public Deprecation Deprecated { get; internal set; }

        /// <summary>True for a list of names, written <c>[index, show]</c> in .kscript and <c>"index, show"</c> in XML (see <see cref="NameList"/>).</summary>
        public bool NameList { get; init; }

        /// <summary>A list of names: <see cref="NameList"/>, or any list of function names.</summary>
        public bool IsNameList => NameList || Kind == AttributeKind.FunctionReferences;
    }

    public sealed class ElementInfo
    {
        public ElementInfo(string name, string description, ContentKind content, params AttributeInfo[] attributes)
        {
            Name = name;
            Description = description;
            Content = content;
            Attributes = attributes;
        }

        public string Name { get; }
        public string Description { get; }
        public ContentKind Content { get; }
        public IReadOnlyList<AttributeInfo> Attributes { get; internal set; }

        public ElementContext Context { get; init; } = ElementContext.Everywhere;

        /// <summary>An example shown in hover.</summary>
        public string Example { get; init; }

        /// <summary>Set when the element is deprecated; it still works, with a warning.</summary>
        public Deprecation Deprecated { get; internal set; }

        /// <summary>Notes for coding agents (<c>kscript agents</c>), or null.</summary>
        public AgentNotes Agent { get; set; }

        /// <summary>Elements this one must directly follow (for <c>elif</c>/<c>else</c>), or be inside (for <c>case</c>).</summary>
        public IReadOnlyList<string> MustFollow { get; init; }

        public IReadOnlyList<string> MustBeInside { get; init; }

        /// <summary>True if <see cref="MustBeInside"/> may be any ancestor (a route inside an &lt;if&gt;), not only the parent.</summary>
        public bool InsideAnyAncestor { get; init; }

        /// <summary>
        /// True if the element's content runs like a function body, so &lt;return&gt; works inside it (a route
        /// answered by the statements inside it).
        /// </summary>
        public bool IsFunctionBody { get; init; }

        /// <summary>An element name without the KScript. prefix: KScript.Routes is Routes.</summary>
        public static string BareName(string name) =>
            name != null && name.StartsWith("KScript.", StringComparison.Ordinal) ? name.Substring(8) : name;

        /// <summary>True for loops, which make <c>&lt;break/&gt;</c> and <c>&lt;continue/&gt;</c> valid inside.</summary>
        public bool IsLoop { get; init; }

        /// <summary>For text content: which parts are used as written, such as LaTeX maths whose braces aren't <c>{ }</c> values.</summary>
        public TextLiterals LiteralText { get; init; }

        /// <summary>True if attributes other than the listed ones are allowed (they become variables, as in <c>&lt;layout&gt;</c>).</summary>
        public bool AllowsExtraAttributes { get; init; }

        /// <summary>The type of the variable its declare-variable attribute (into:) makes, when not text.</summary>
        public string DeclaresType { get; init; }

        /// <summary>True if the element is in the KScript 2 design but no package implements it yet.</summary>
        public bool Planned => Package == null;

        /// <summary>
        /// True if, in an HTML page, an HTML element with this name takes precedence (input, menu, select...).
        /// </summary>
        public bool HtmlWins { get; init; }

        /// <summary>The package that provides the element (KScript.Core, KScript.Terminal...), or null while it is only planned.</summary>
        public string Package { get; internal set; }

        public AttributeInfo GetAttribute(string name) =>
            Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A v1 element or attribute that KScript 2 removed, with what to use instead.</summary>
    public sealed class RetiredInfo
    {
        public RetiredInfo(string name, string replacement, string rename = null)
        {
            Name = name;
            Replacement = replacement;
            Rename = rename;
        }

        public string Name { get; }

        /// <summary>A sentence saying what to write instead.</summary>
        public string Replacement { get; }

        /// <summary>If the fix is a plain rename, the new name (offered as a quick fix).</summary>
        public string Rename { get; }

        /// <summary>
        /// True for a name KScript 2 itself changed (<c>before-each</c> is now <c>before_each</c>), rather than
        /// KScript 1 syntax that was removed. <see cref="Replacement"/> then finishes the sentence: "is now before_each."
        /// </summary>
        public bool Renamed { get; init; }

        /// <summary>The whole message for something written the old way: what it was, and what to write instead.</summary>
        public string Message(string written) =>
            Renamed ? $"{written} {Replacement}" : $"{written} was removed in KScript 2. {Replacement}";
    }
}
