using KScript.Syntax.Text;

namespace KScript.Syntax
{
    public enum DiagnosticSeverity
    {
        Error = 1,
        Warning = 2,
        Information = 3,
        Hint = 4,
    }

    /// <summary>A replacement of a span of the document.</summary>
    public sealed class TextChange
    {
        public TextChange(TextSpan span, string newText)
        {
            Span = span;
            NewText = newText;
        }

        public TextSpan Span { get; }
        public string NewText { get; }
    }

    /// <summary>A one-click fix offered for a diagnostic.</summary>
    public sealed class QuickFix
    {
        public QuickFix(string title, params TextChange[] changes)
        {
            Title = title;
            Changes = changes;
        }

        public string Title { get; }
        public IReadOnlyList<TextChange> Changes { get; }
    }

    /// <summary>A problem found in a document.</summary>
    public sealed class Diagnostic
    {
        public Diagnostic(TextSpan span, DiagnosticSeverity severity, string code, string message, IReadOnlyList<QuickFix> fixes = null)
        {
            Span = span;
            Severity = severity;
            Code = code;
            Message = message;
            Fixes = fixes ?? Array.Empty<QuickFix>();
        }

        public TextSpan Span { get; }
        public DiagnosticSeverity Severity { get; }

        /// <summary>A stable identifier, such as <c>KS1001</c>, listed in <see cref="DiagnosticCodes"/>.</summary>
        public string Code { get; }

        public string Message { get; }
        public IReadOnlyList<QuickFix> Fixes { get; }

        public override string ToString() => $"{Severity} {Code} {Span}: {Message}";
    }

    /// <summary>Diagnostic codes. KS1xxx are markup, KS2xxx expressions, KS3xxx elements, KS4xxx names and KS5xxx calls.</summary>
    public static class DiagnosticCodes
    {
        public const string MarkupSyntax = "KS1001";
        public const string MismatchedEndTag = "KS1002";
        public const string UnclosedElement = "KS1003";
        public const string InvalidAttribute = "KS1004";
        public const string DuplicateAttribute = "KS1005";

        public const string ExpressionSyntax = "KS2001";
        public const string AssignmentNotAllowed = "KS2002";
        public const string StatementSyntax = "KS2003";
        public const string BareIdentifier = "KS2004";

        public const string UnknownElement = "KS3001";
        public const string RetiredElement = "KS3002";
        public const string UnknownAttribute = "KS3003";
        public const string RetiredAttribute = "KS3004";
        public const string MissingAttribute = "KS3005";
        public const string InvalidAttributeValue = "KS3006";
        public const string MisplacedElement = "KS3007";
        public const string StrayText = "KS3008";
        public const string MissingImport = "KS3009";
        public const string UnknownPackage = "KS3010";

        public const string UndefinedVariable = "KS4001";
        public const string SetUndeclared = "KS4002";
        public const string AssignConst = "KS4003";
        public const string DuplicateDeclaration = "KS4004";
        public const string UndefinedFunction = "KS4005";
        public const string RetiredSigil = "KS4006";

        public const string UnknownCommand = "KS5001";
        public const string RetiredCommand = "KS5002";
        public const string UnknownMethod = "KS5003";
        public const string ArgumentCount = "KS5004";
        public const string TypeMismatch = "KS5005";
        public const string PlannedFeature = "KS5006";
    }
}
