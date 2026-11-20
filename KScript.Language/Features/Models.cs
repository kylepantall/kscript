using KScript.Language.Analysis;
using KScript.Syntax.Text;

namespace KScript.Language.Features
{
    public enum CompletionKind
    {
        Element,
        Attribute,
        Value,
        Variable,
        Parameter,
        Constant,
        Function,
        Command,
        Method,
        Field,
        Keyword,
        File,
        Snippet,
        Folder,
    }

    public sealed class CompletionItem
    {
        public string Label { get; init; }
        public CompletionKind Kind { get; init; }
        public string Detail { get; init; }

        /// <summary>Markdown.</summary>
        public string Documentation { get; init; }

        /// <summary>The text to insert (a snippet when <see cref="IsSnippet"/>), or null to insert the label.</summary>
        public string InsertText { get; init; }

        public bool IsSnippet { get; init; }

        /// <summary>The span the insert text replaces (the word typed so far).</summary>
        public TextSpan ReplaceSpan { get; init; }

        public string SortText { get; init; }
        public string FilterText { get; init; }
        public bool Deprecated { get; init; }

        public override string ToString() => $"{Kind} {Label}";
    }

    public sealed class HoverResult
    {
        public HoverResult(string markdown, TextSpan span)
        {
            Markdown = markdown;
            Span = span;
        }

        public string Markdown { get; }
        public TextSpan Span { get; }
    }

    public sealed class SignatureInfo
    {
        public string Label { get; init; }
        public string Documentation { get; init; }
        public IReadOnlyList<(string Label, string Documentation)> Parameters { get; init; }

        /// <summary>Per parameter: its name, the enum values it takes (or null), and whether it is a yes/no flag.</summary>
        internal IReadOnlyList<(string Name, IReadOnlyList<string> Values, bool Flag)> Details { get; init; }
    }

    public sealed class SignatureHelpResult
    {
        public List<SignatureInfo> Signatures { get; } = new List<SignatureInfo>();
        public int ActiveSignature { get; set; }
        public int ActiveParameter { get; set; }
    }

    /// <summary>A place in a document: this one (when <see cref="Path"/> is null) or another file.</summary>
    public sealed class SourceLocation
    {
        public SourceLocation(string path, TextSpan span)
        {
            Path = path;
            Span = span;
        }

        public SourceLocation(string path, LinePosition start, LinePosition end)
        {
            Path = path;
            Start = start;
            End = end;
            HasLinePositions = true;
        }

        public string Path { get; }
        public TextSpan Span { get; }
        public bool HasLinePositions { get; }
        public LinePosition Start { get; }
        public LinePosition End { get; }
    }

    public enum OutlineKind
    {
        Function,
        Variable,
        Constant,
        Module,
        Element,
    }

    public sealed class OutlineItem
    {
        public string Name { get; init; }
        public string Detail { get; init; }
        public OutlineKind Kind { get; init; }
        public TextSpan Span { get; init; }
        public TextSpan SelectionSpan { get; init; }
        public List<OutlineItem> Children { get; } = new List<OutlineItem>();
    }

    public enum SemanticTokenType
    {
        Variable,
        Parameter,
        Function,
        Method,
        Property,
        Keyword,
        Operator,
        String,
        Number,
        Namespace,
        Type,
        Regexp,
    }

    [Flags]
    public enum SemanticTokenModifiers
    {
        None = 0,
        Declaration = 1,
        Readonly = 2,
        DefaultLibrary = 4,
    }

    public readonly struct SemanticToken
    {
        public SemanticToken(TextSpan span, SemanticTokenType type, SemanticTokenModifiers modifiers)
        {
            Span = span;
            Type = type;
            Modifiers = modifiers;
        }

        public TextSpan Span { get; }
        public SemanticTokenType Type { get; }
        public SemanticTokenModifiers Modifiers { get; }
        public override string ToString() => $"{Type} {Span}";
    }

    public sealed class RenameResult
    {
        public string Error { get; init; }
        public List<TextChange> Changes { get; } = new List<TextChange>();
    }
}
