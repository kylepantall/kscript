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

    /// <summary>Diagnostic codes. KS1xxx are markup, KS2xxx expressions, KS3xxx elements, KS4xxx names, KS5xxx calls and KS6xxx style.</summary>
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

        /// <summary>A 'single quoted' string that holds <c>$name</c> or <c>{ }</c>: single quotes keep text as written, double quotes fill it in.</summary>
        public const string TextNotFilledIn = "KS2005";

        /// <summary>A bare word that more than one declared type's pattern fits (<c>key = abc</c>), so which type it is has to be written.</summary>
        public const string AmbiguousBareWord = "KS2006";

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
        public const string DocParameter = "KS4007";
        public const string Deprecated = "KS4008";

        public const string UnknownCommand = "KS5001";
        public const string RetiredCommand = "KS5002";
        public const string UnknownMethod = "KS5003";
        public const string ArgumentCount = "KS5004";
        public const string TypeMismatch = "KS5005";
        /// <summary>An element or command the catalog describes but no package runs yet (render, on, @dom).</summary>
        public const string PlannedFeature = "KS5006";
        public const string UnknownParameter = "KS5007";
        public const string NamedArgument = "KS5008";
        public const string InvalidArgumentValue = "KS5009";
        public const string UnknownField = "KS5010";
        /// <summary>A catch, throw or extends that names an error type nobody declared.</summary>
        public const string UnknownErrorType = "KS5011";

        /// <summary>A literal that can't be the type it is written for: a bad email, an Age of 200, a word not in an enum.</summary>
        public const string InvalidTypedValue = "KS5012";

        /// <summary>A KScript.Data query, create or update that names a column the model doesn't have.</summary>
        public const string UnknownColumn = "KS5013";

        /// <summary>A model whose table the migrations don't make as it is now: kscript db diff writes the migration.</summary>
        public const string ModelDrift = "KS5014";

        /// <summary>An else or elif in a script, where an early return, continue or break reads better.</summary>
        public const string PreferEarlyExit = "KS6001";

        /// <summary>A function that nothing calls: not its own file, and not any file in the project that imports it.</summary>
        public const string UnusedFunction = "KS6002";

        /// <summary>A variable or constant whose value is never read.</summary>
        public const string UnusedVariable = "KS6003";

        /// <summary>A function or lambda parameter the body never reads.</summary>
        public const string UnusedParameter = "KS6004";

        /// <summary>An import (a script, a name from one, or a package) that nothing uses.</summary>
        public const string UnusedImport = "KS6005";

        /// <summary>A list of names written as one string in a .kscript file (<c>use: "log, cors"</c>) rather than an array (<c>use: [log, cors]</c>).</summary>
        public const string PreferArrayList = "KS6006";

        /// <summary>A function parameter with no type and no default: <c>fn cached(key)</c> rather than <c>fn cached(key: string)</c>.</summary>
        public const string MissingParameterType = "KS6007";

        /// <summary>A script in a project that declares more than one type, model, enum or error type: each goes in its own file.</summary>
        public const string OneTypePerFile = "KS6008";

        /// <summary>Text given to a .kscript argument as a bare word; text goes in quotes: <c>title: "Report"</c>.</summary>
        public const string QuoteText = "KS6009";

        /// <summary>An assignment or lambda parameter written with <c>$</c>; names are declared bare: <c>total = 3</c>, <c>fn(n) =&gt; $n * 2</c>.</summary>
        public const string BareDeclaration = "KS6010";

        /// <summary>yes/no (or on/off) in a .kscript file; booleans are <c>true</c> and <c>false</c>.</summary>
        public const string BoolWords = "KS6011";

        /// <summary>A second spelling the parser accepts: <c>else if</c>, <c>key = value</c> in arguments, <c>catch A, B</c>, <c>lt</c>/<c>gt</c> for comparisons in .kscript.</summary>
        public const string OneSpelling = "KS6012";

        /// <summary>A name that doesn't follow the naming rules: snake_case for functions, variables, fields and arguments; PascalCase for types.</summary>
        public const string NamingStyle = "KS6013";

        /// <summary>A type name written with <c>$</c> (<c>$Repo</c>), or an optional field marked on its type (<c>age: int?</c>) rather than its name.</summary>
        public const string TypeForm = "KS6014";

        /// <summary>A length of time written without its unit (<c>timeout: 30</c>) or in quotes (<c>ttl: '10m'</c>); a duration is <c>30s</c>.</summary>
        public const string DurationUnit = "KS6018";

        /// <summary>Where a code is explained: its section of docs/errors.md.</summary>
        public static string HelpUrl(string code) =>
            "https://github.com/kylepantall/kscript/blob/master/docs/errors.md#" + (code ?? string.Empty).ToLowerInvariant();

        /// <summary>
        /// True for the unused-code rules (KS6002-KS6005). Editors show their code faded, as unnecessary, as well as
        /// (or instead of) underlining it.
        /// </summary>
        public static bool IsUnnecessary(string code) =>
            code == UnusedFunction || code == UnusedVariable || code == UnusedParameter || code == UnusedImport;
    }
}
