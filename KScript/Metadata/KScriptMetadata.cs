using System;

namespace KScript.Metadata
{
    // These attributes describe the language to tools. The language server (KScript.Language) reads them by
    // reflection, so a command or element documented here gets completion, hover, signature help and argument
    // checks with no other change. Contract tests in KScript.Language.Tests fail when a command or element has
    // no metadata, so new built-ins can't silently go undocumented.

    /// <summary>How an attribute's (or argument's) value is read.</summary>
    public enum KScriptValueKind
    {
        /// <summary>One expression: <c>test="$n lt 3"</c>.</summary>
        Expression,

        /// <summary>A comma separated list of expressions: <c>is="2, 3"</c>.</summary>
        ExpressionList,

        /// <summary>Text with <c>{ }</c> interpolation: <c>href="/people/{$p.id}"</c>.</summary>
        Template,

        /// <summary>A variable name that the element declares: <c>name="count"</c>.</summary>
        DeclareVariable,

        /// <summary>One or more variable names the element declares, comma separated: <c>each="key, value"</c>.</summary>
        DeclareVariables,

        /// <summary>A variable that must already exist: <c>&lt;set name="count"&gt;</c>.</summary>
        VariableReference,

        /// <summary>Function parameters: <c>params="who, greeting = 'Hello'"</c>.</summary>
        Parameters,

        /// <summary>The name of a function the element declares.</summary>
        DeclareFunction,

        /// <summary>The name of a <c>&lt;fn&gt;</c> function.</summary>
        FunctionReference,

        /// <summary>A comma separated list of function names.</summary>
        FunctionReferences,

        /// <summary>A file path relative to the document.</summary>
        Path,

        /// <summary>A CSS selector.</summary>
        Selector,

        /// <summary>One of a fixed set of words (see the attribute's options).</summary>
        Enum,

        /// <summary>yes/no (also true/false).</summary>
        Bool,

        /// <summary>Plain text.</summary>
        Text,
    }

    /// <summary>What an element's content holds.</summary>
    public enum KScriptContentKind
    {
        /// <summary>Nothing: the element is normally self-closing.</summary>
        Empty,

        /// <summary>KScript elements to run.</summary>
        Statements,

        /// <summary>Text with <c>{ }</c> and bare <c>$name</c> references.</summary>
        Template,

        /// <summary>A single expression.</summary>
        Expression,

        /// <summary>Assignment statements separated by <c>;</c>.</summary>
        AssignmentStatements,

        /// <summary>HTML to output, with KScript elements and <c>{ }</c> inside.</summary>
        Markup,
    }

    /// <summary>Where an element can be used.</summary>
    [Flags]
    public enum KScriptElementContext
    {
        Script = 1,
        Page = 2,
        Everywhere = Script | Page,
    }

    /// <summary>
    /// Documents a command (<c>@name()</c>) or element class: what it does, an example, and for commands the
    /// type of value it returns (any, string, number, bool, list, map, fn, file, date...).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class KScriptDocAttribute : Attribute
    {
        public KScriptDocAttribute(string description) => Description = description;

        public string Description { get; }

        /// <summary>A short usage example, shown in hover.</summary>
        public string Example { get; set; }

        /// <summary>For commands: the type of the result. Defaults to string.</summary>
        public string Returns { get; set; } = "string";
    }

    /// <summary>
    /// Documents one parameter of a command. The name must match a constructor parameter (checked by the contract tests).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class KScriptParamAttribute : Attribute
    {
        public KScriptParamAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }
        public string Description { get; }

        /// <summary>How the argument is used (for example <see cref="KScriptValueKind.Path"/>).</summary>
        public KScriptValueKind Kind { get; set; } = KScriptValueKind.Expression;
    }

    /// <summary>
    /// Marks a v1 element, command or attribute that KScript 2 removed, with what to write instead. Tools report its
    /// use as an error that names the replacement, and offer <see cref="Rename"/> as a quick fix.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class KScriptRetiredAttribute : Attribute
    {
        public KScriptRetiredAttribute(string replacement) => Replacement = replacement;

        /// <summary>A sentence saying what to write instead.</summary>
        public string Replacement { get; }

        /// <summary>If the fix is a plain rename, the new element or attribute name.</summary>
        public string Rename { get; set; }
    }

    /// <summary>
    /// Declares an element's shape for tools. Put it on the class that implements the element; it overrides the
    /// built-in description in the language catalog, so the interpreter and the IDE can't drift apart.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class KScriptElementAttribute : Attribute
    {
        public KScriptElementAttribute(KScriptContentKind content) => Content = content;

        public KScriptContentKind Content { get; }

        public KScriptElementContext Context { get; set; } = KScriptElementContext.Everywhere;

        /// <summary>Elements this one must directly follow, comma separated (<c>"if, elif"</c> for <c>else</c>).</summary>
        public string MustFollow { get; set; }

        /// <summary>Elements this one must be inside, comma separated (<c>"match"</c> for <c>case</c>).</summary>
        public string MustBeInside { get; set; }

        /// <summary>True for loops: <c>&lt;break/&gt;</c> and <c>&lt;continue/&gt;</c> are valid inside.</summary>
        public bool IsLoop { get; set; }

        /// <summary>True if attributes other than the declared ones are allowed (they become variables).</summary>
        public bool AllowsExtraAttributes { get; set; }

        /// <summary>True if, in an HTML page, an HTML element with the same name takes precedence.</summary>
        public bool HtmlWins { get; set; }
    }

    /// <summary>
    /// Declares an attribute of an element that isn't backed by a [KScriptProperty] property, or refines one that is.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class KScriptAttributeAttribute : Attribute
    {
        public KScriptAttributeAttribute(string name, KScriptValueKind kind, string description)
        {
            Name = name;
            Kind = kind;
            Description = description;
        }

        public string Name { get; }
        public KScriptValueKind Kind { get; }
        public string Description { get; }
        public bool Required { get; set; }

        /// <summary>Accepted values for <see cref="KScriptValueKind.Enum"/>, comma separated.</summary>
        public string Options { get; set; }
    }

    /// <summary>
    /// Refines how a [KScriptProperty] property is read by tools (by default its kind is inferred from
    /// [KScriptAcceptedOptions], or treated as text with <c>{ }</c>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class KScriptValueAttribute : Attribute
    {
        public KScriptValueAttribute(KScriptValueKind kind) => Kind = kind;

        public KScriptValueKind Kind { get; }
    }
}
