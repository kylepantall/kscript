namespace KScript.Metadata
{
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

        /// <summary>Another attribute that can stand in for this one when it is required (<c>value</c> or element text).</summary>
        public bool SatisfiedByContent { get; init; }
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

        /// <summary>Elements this one must directly follow (for <c>elif</c>/<c>else</c>), or be inside (for <c>case</c>).</summary>
        public IReadOnlyList<string> MustFollow { get; init; }

        public IReadOnlyList<string> MustBeInside { get; init; }

        /// <summary>True for loops, which make <c>&lt;break/&gt;</c> and <c>&lt;continue/&gt;</c> valid inside.</summary>
        public bool IsLoop { get; init; }

        /// <summary>True if attributes other than the listed ones are allowed (they become variables, as in <c>&lt;layout&gt;</c>).</summary>
        public bool AllowsExtraAttributes { get; init; }

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
    }
}
