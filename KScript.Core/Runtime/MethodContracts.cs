using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Runtime
{
    /// <summary>An HTML template and the lexical scope in which the component returned it.</summary>
    public sealed class ComponentTemplate
    {
        internal ComponentTemplate(StringBlock block, Scope scope, Module module, DecodedText origin)
        {
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

    public sealed record MethodSignature(string Name, string Returns, IReadOnlyList<Parameter> Parameters, bool Optional)
    {
        public static readonly MethodSignature Render = new("render", ComponentScript.Contract, Array.Empty<Parameter>(), false);

        public static MethodSignature From(MarkupElement fn) => new(fn.GetAttributeValue("name")?.Trim(),
            fn.GetAttributeValue("returns")?.Trim(), Parameter.ParseList(fn.GetAttributeValue("params"), out _),
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
