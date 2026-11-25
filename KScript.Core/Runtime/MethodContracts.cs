using KScript.Metadata;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Runtime
{
    /// <summary>An HTML template and the lexical scope in which the component returned it.</summary>
    public sealed class ComponentTemplate
    {
        internal ComponentTemplate(StringBlock block, Scope scope, Module module, DecodedText origin)
        {
            var fragment = MarkupParser.ParsePageFragment(module.Source, origin, TextSpan.FromBounds(block.ContentStart, block.ContentEnd));
            if (fragment.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is Diagnostic error)
            {
                LinePosition at = module.Source.GetPosition(error.Span.Start);
                throw new KScriptException(error.Message, "Syntax") { File = module.Path, Line = at.Line + 1, Column = at.Character + 1 };
            }
            Html = block.Value;
            Scope = scope;
            Module = module;
            FirstLine = module.Source.GetPosition(origin.ToSource(block.ContentStart)).Line + 1;
        }

        public string Html { get; }
        public Scope Scope { get; }
        public Module Module { get; }
        public int FirstLine { get; }
    }

    /// <summary>
    /// The Markdown or LaTeX a component's <c>render</c> returned for a document, and the scope it was returned in.
    /// </summary>
    public sealed class ComponentSource
    {
        /// <summary>One text for each block, however often it is drawn: the interpreter keeps what it parsed by it.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DecodedText, Tuple<DecodedText, List<(int Open, int Close, DecodedText Value)>>> Read = new();

        private readonly DecodedText content;
        private readonly List<(int Open, int Close, DecodedText Value)> values;
        private readonly Scope scope;
        private readonly Module module;

        private ComponentSource(StringBlock block, Scope scope, Module module, DecodedText origin)
        {
            Latex = StringBlock.ValueType(block.Tag) == KType.Latex;
            (content, values) = Read.GetValue(origin, o =>
            {
                DecodedText text = ComponentScript.BlockText(block, o);
                return Tuple.Create(text, Latex ? ComponentScript.LatexValues(text).ToList() : null);
            });
            this.scope = scope;
            this.module = module;
        }

        /// <summary>What a <c>return</c> (or <c>=&gt;</c>) of <paramref name="function"/> gives when its value is a document block, or null.</summary>
        internal static ComponentSource From(MarkupElement function, DecodedText origin, Scope scope, Module module) =>
            ComponentScript.SourceBlock(function, origin) is { Error: null } block ? new ComponentSource(block, scope, module, origin) : null;

        /// <summary>True for LaTeX, false for Markdown.</summary>
        public bool Latex { get; }

        /// <summary>
        /// The source with its values put in. Markdown takes <c>{ }</c> as text in a script does. In LaTeX the
        /// braces stay, as LaTeX's, and each value goes through <paramref name="escape"/>.
        /// </summary>
        public string Fill(Interpreter interpreter, Func<string, string> escape)
        {
            if (!Latex) return interpreter.Interpolate(content, scope, module, bareReferences: false, null, Values.ToStr, TextLiterals.Maths);
            string text = content.Value;
            var filled = new System.Text.StringBuilder(text.Length + 16);
            int at = 0;
            foreach ((int open, int close, DecodedText value) in values)
            {
                filled.Append(text, at, open + 1 - at).Append(escape(Values.ToStr(interpreter.EvaluateText(value, scope, module))));
                at = close;
            }
            return filled.Append(text, at, text.Length - at).ToString();
        }
    }

    internal sealed record ComponentStyle(string Text, string Language, Module Module);

    public sealed record MethodSignature(string Name, string Returns, IReadOnlyList<Parameter> Parameters, bool Optional)
    {
        public static readonly MethodSignature Render = new("render", ComponentScript.Contract, Array.Empty<Parameter>(), false);

        public static IReadOnlyList<MethodSignature> Component { get; } = new[]
        {
            Render,
            new MethodSignature("style", ComponentScript.StyleContract, Array.Empty<Parameter>(), true),
        };

        public static MethodSignature From(MarkupElement fn) => new(fn.GetAttributeValue("name")?.Trim(),
ComponentScript.Returns(fn), Parameter.ParseList(fn.GetAttributeValue("params"), out _),
            fn.GetAttribute("optional") != null);

        public string Display => $"fn {Name}({string.Join(", ", Parameters.Select(p => p.Type == null ? p.Name : p.Name + ": " + p.Type))})"
            + (Returns == null ? "" : ": " + Returns);

        public string Problem(MarkupElement implementation)
        {
            if (implementation == null)
                return Optional ? null : $"implement {Display}";
            var actual = From(implementation);
            if (Returns != actual.Returns || Parameters.Count != actual.Parameters.Count
                || !Parameters.Zip(actual.Parameters).All(p => p.First.Name == p.Second.Name
                    && (p.First.Type ?? "any") == (p.Second.Type ?? "any") && (!p.First.HasDefault || p.Second.HasDefault)))
                return $"match the signature {Display}";
            return null;
        }
    }
}
