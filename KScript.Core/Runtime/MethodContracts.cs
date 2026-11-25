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
