using KScript.Language.Catalog;
using KScript.Syntax.Markup;
using KScript.Syntax;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    public enum SymbolKind
    {
        Variable,
        Constant,
        LoopVariable,
        Parameter,
        Function,
        Import,
        BuiltIn,
        FrontMatter,
    }

    /// <summary>What the analyzer knows about a value: its type, and for maps and lists of maps, their fields.</summary>
    public sealed class TypeShape
    {
        /// <summary>The shared "could be anything" shape. It is frozen: give a symbol its own shape before adding fields.</summary>
        public static readonly TypeShape Unknown = new TypeShape(KType.Any, frozen: true);

        public TypeShape(string type) => Type = type;

        private TypeShape(string type, bool frozen)
        {
            Type = type;
            if (frozen)
            {
                Fields = new System.Collections.ObjectModel.ReadOnlyDictionary<string, FieldInfo>(new Dictionary<string, FieldInfo>());
            }
        }

        public bool IsFrozen => Fields.IsReadOnly;

        public string Type { get; }

        /// <summary>For records made by a &lt;type&gt;, and for the type itself: the type's name, shown instead of its fields.</summary>
        public string Name { get; init; }

        /// <summary>
        /// True when the type was written (<c>person: int</c>, <c>type="string"</c>), so the value is held to it: a
        /// declared number has no string methods, though a number that merely came from somewhere may be text.
        /// </summary>
        public bool Declared { get; init; }

        /// <summary>True for records of a &lt;type&gt; without extra="keep": reading a field it doesn't list is a mistake.</summary>
        public bool Closed { get; init; }

        /// <summary>
        /// A more exact kind whose methods come first: "model" for a KScript.Data model (<c>$User->where()</c>), "record"
        /// for its records (<c>$user->save()</c>), and for queries on a model. Null for everything else.
        /// </summary>
        public string Kind { get; init; }

        /// <summary>For a &lt;type&gt; value (<c>$Repo</c>): the shape of the records it makes.</summary>
        public TypeShape Instance { get; init; }

        /// <summary>For records of a &lt;type&gt;: the methods its &lt;fn&gt; children declare (<c>$size->width_in(cm)</c>).</summary>
        public IDictionary<string, Symbol> Methods { get; } = new Dictionary<string, Symbol>(StringComparer.Ordinal);

        /// <summary>Known fields of a map (from a literal, or of a built-in type such as $request).</summary>
        public IDictionary<string, FieldInfo> Fields { get; } = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

        /// <summary>For lists: the shape of the items.</summary>
        public TypeShape Element
        {
            get => element;
            set => element = IsFrozen ? throw new InvalidOperationException("TypeShape.Unknown is shared and can't be changed.") : value;
        }

        private TypeShape element;

        /// <summary>
        /// The shape written like a type: <c>string</c>, <c>list&lt;number&gt;</c>, <c>{ name: string, age: number }</c>
        /// or <c>list&lt;{ name: string }&gt;</c>. Deep or wide maps are cut short with <c>…</c>.
        /// </summary>
        public string Display(int depth = 0)
        {
            if (Name != null)
            {
                return Type == KType.Type ? (Kind == KType.Model ? "model " : "type ") + Name : Type == KType.Query ? "query of " + Name : Name;
            }
            if (Type == KType.List)
            {
                return Element == null || Element.Type == KType.Any ? "list" : $"list<{Element.Display(depth + 1)}>";
            }
            if (Type == KType.Map && Fields.Count > 0)
            {
                if (depth >= 2)
                {
                    return "{ … }";
                }
                const int shown = 6;
                IEnumerable<string> parts = Fields.Values.Take(shown).Select(f => f.Name + ": " + f.Shape.Display(depth + 1));
                return "{ " + string.Join(", ", parts) + (Fields.Count > shown ? ", …" : string.Empty) + " }";
            }
            return Type;
        }

        /// <summary>
        /// Like <see cref="Display"/>, but a map with several fields is spread over lines, for hovers:
        /// <code>{
        ///   name: string,
        ///   address: { city: string }
        /// }</code>
        /// </summary>
        public string Pretty(string indent = "", int depth = 0)
        {
            string flat = Display();
            if (flat.Length <= 48 || depth >= 3 || Name != null)
            {
                return flat;
            }
            if (Type == KType.List && Element != null)
            {
                return "list<" + Element.Pretty(indent, depth + 1) + ">";
            }
            if (Type == KType.Map && Fields.Count > 0)
            {
                string inner = indent + "  ";
                const int shown = 24;
                IEnumerable<string> lines = Fields.Values.Take(shown).Select(f => inner + f.Name + ": " + f.Shape.Pretty(inner, depth + 1));
                return "{\n" + string.Join(",\n", lines) + (Fields.Count > shown ? ",\n" + inner + "…" : string.Empty) + "\n" + indent + "}";
            }
            return flat;
        }

        /// <summary>
        /// A short form for inline labels (inlay hints): <see cref="Display"/> when it fits, otherwise a map shows only
        /// its first field names: <c>{ grid, generation, history, … }</c>.
        /// </summary>
        public string Brief(int width = 40)
        {
            string full = Display();
            if (full.Length <= width)
            {
                return full;
            }
            if (Type == KType.List && Element != null)
            {
                return "list<" + Element.Brief(width - 6) + ">";
            }
            if (Type == KType.Map && Fields.Count > 0)
            {
                List<string> names = new List<string>();
                int length = 4;
                foreach (string name in Fields.Keys)
                {
                    if (names.Count > 0 && length + name.Length + 2 > width)
                    {
                        break;
                    }
                    names.Add(name);
                    length += name.Length + 2;
                }
                return "{ " + string.Join(", ", names) + (names.Count < Fields.Count ? ", …" : string.Empty) + " }";
            }
            return full;
        }

        /// <summary>True if nothing useful is known (any or null).</summary>
        public bool IsVague => Type == KType.Any || Type == KType.Null;

        public override string ToString() => Display();
    }

    public sealed class FieldInfo
    {
        public FieldInfo(string name, TypeShape shape, TextSpan? declaration, string description = null)
        {
            Name = name;
            Shape = shape;
            Declaration = declaration;
            Description = description;
        }

        public string Name { get; }
        public TypeShape Shape { get; }

        /// <summary>Where the key is written in a map literal, if it came from one.</summary>
        public TextSpan? Declaration { get; }

        public string Description { get; }
    }

    /// <summary>A declared name: a variable, parameter, function or import.</summary>
    public sealed class Symbol
    {
        public string Name { get; internal set; }
        public SymbolKind Kind { get; internal set; }

        /// <summary>The span of the name where it is declared (empty for built-ins).</summary>
        public TextSpan DeclarationSpan { get; internal set; }

        /// <summary>The element that declares it, if any.</summary>
        public MarkupElement DeclaringElement { get; internal set; }

        public TypeShape Shape { get; internal set; } = TypeShape.Unknown;

        /// <summary>For functions: the shape of what they return, worked out from <c>value</c> or their <c>&lt;return&gt;</c>s.</summary>
        public TypeShape ReturnShape { get; internal set; } = TypeShape.Unknown;

        public string Description { get; internal set; }

        /// <summary>The description read as a doc comment (summary, @param, @returns, @example, @deprecated), or null.</summary>
        public DocComment Doc => DocComment.Parse(Description);

        /// <summary>For functions: their parameters, in order, and how many are required.</summary>
        public List<FunctionParameter> Parameters { get; } = new List<FunctionParameter>();

        public int RequiredParameterCount => Parameters.Count(p => !p.HasDefault);

        /// <summary>For &lt;enum&gt; types: their values, in order. A parameter typed with the enum takes them bare.</summary>
        public List<string> EnumValues { get; internal set; }

        /// <summary>For imports: the file they come from (absolute, when the document has a path).</summary>
        public string SourcePath { get; internal set; }

        public bool IsConst => Kind == SymbolKind.Constant;

        /// <summary>True if the user can rename it (not a built-in or front matter key).</summary>
        public bool CanRename => Kind != SymbolKind.BuiltIn;

        public string Signature => Kind == SymbolKind.Function
            ? $"@{Name}({string.Join(", ", Parameters.Select(p => p.Label))})"
            : "$" + Name;

        /// <summary>The signature with the return type when it is known: <c>@double(x: int): number</c>.</summary>
        public string TypedSignature => Kind == SymbolKind.Function && ReturnShape.Type != KType.Any ? Signature + ": " + ReturnShape.Display() : Signature;

        public override string ToString() => $"{Kind} {Name}";
    }

    public sealed class FunctionParameter
    {
        public string Name { get; internal set; }
        public string Type { get; internal set; }
        public string Default { get; internal set; }
        public bool HasDefault => Default != null;
        public TextSpan Span { get; internal set; }
        public string Label => Name + (Type != null ? ": " + Type : string.Empty) + (HasDefault ? " = " + Default : string.Empty);
    }

    /// <summary>A use of a symbol in the document.</summary>
    public sealed class SymbolReference
    {
        public SymbolReference(Symbol symbol, TextSpan span, bool isDeclaration, bool isWrite)
        {
            Symbol = symbol;
            Span = span;
            IsDeclaration = isDeclaration;
            IsWrite = isWrite;
        }

        public Symbol Symbol { get; }

        /// <summary>The name only (for variables, without the '$'; for commands, without the '@').</summary>
        public TextSpan Span { get; }

        public bool IsDeclaration { get; }
        public bool IsWrite { get; }
    }

    /// <summary>A region of a scope in which some names are visible.</summary>
    public sealed class Scope
    {
        public Scope(Scope parent, TextSpan span)
        {
            Parent = parent;
            Span = span;
            parent?.Children.Add(this);
        }

        public Scope Parent { get; }
        public TextSpan Span { get; }
        public List<Scope> Children { get; } = new List<Scope>();

        /// <summary>Symbols declared directly in this scope, with the offset from which each is visible.</summary>
        public List<(Symbol Symbol, int VisibleFrom)> Symbols { get; } = new List<(Symbol, int)>();

        public void Declare(Symbol symbol, int visibleFrom) => Symbols.Add((symbol, visibleFrom));

        public Symbol Lookup(string name, int offset)
        {
            for (Scope s = this; s != null; s = s.Parent)
            {
                for (int i = s.Symbols.Count - 1; i >= 0; i--)
                {
                    (Symbol sym, int from) = s.Symbols[i];
                    if (sym.Name == name && from <= offset)
                    {
                        return sym;
                    }
                }
            }
            return null;
        }

        /// <summary>Every symbol visible at the offset, innermost first, without shadowed duplicates.</summary>
        public IEnumerable<Symbol> VisibleAt(int offset)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (Scope s = this; s != null; s = s.Parent)
            {
                for (int i = s.Symbols.Count - 1; i >= 0; i--)
                {
                    (Symbol sym, int from) = s.Symbols[i];
                    if (from <= offset && seen.Add(sym.Kind + ":" + sym.Name))
                    {
                        yield return sym;
                    }
                }
            }
        }

        /// <summary>The innermost scope containing the offset.</summary>
        public Scope Innermost(int offset)
        {
            foreach (Scope child in Children)
            {
                if (child.Span.Contains(offset))
                {
                    return child.Innermost(offset);
                }
            }
            return this;
        }
    }

    public enum RegionKind
    {
        Expression,
        ExpressionList,
        Statements,
        Template,
    }

    /// <summary>
    /// A piece of the document that holds KScript expressions (an attribute value, element text, a front matter
    /// value), kept so editor features can work inside it.
    /// </summary>
    public sealed class ExpressionRegion
    {
        public RegionKind Kind { get; internal set; }
        public DecodedText Source { get; internal set; }

        /// <summary>For expression, list and statement regions.</summary>
        public ParseResult Parse { get; internal set; }

        /// <summary>For template regions.</summary>
        public Template Template { get; internal set; }

        public MarkupElement Element { get; internal set; }

        /// <summary>The attribute, when the region is an attribute value.</summary>
        public MarkupAttribute Attribute { get; internal set; }

        /// <summary>The scope that names inside the region resolve in.</summary>
        public Scope Scope { get; internal set; }

        public TextSpan SourceSpan => Source.SourceSpan;

        /// <summary>Local spans (in <see cref="Source"/>) that hold expression code, as opposed to literal text.</summary>
        public IEnumerable<TextSpan> CodeSpans
        {
            get
            {
                if (Kind != RegionKind.Template)
                {
                    yield return new TextSpan(0, Source.Value.Length);
                    yield break;
                }
                foreach (TemplateSegment segment in Template.Expressions)
                {
                    yield return segment.Kind == TemplateSegmentKind.Reference
                        ? segment.Span
                        : TextSpan.FromBounds(segment.Span.Start + (segment.Kind == TemplateSegmentKind.RawInterpolation ? 2 : 1),
                            Math.Max(segment.Span.Start + 1, segment.Span.End - (segment.Span.End <= Source.Value.Length && segment.Span.End > 0 && Source.Value[segment.Span.End - 1] == '}' ? 1 : 0)));
                }
            }
        }

        /// <summary>All top-level expression nodes in the region.</summary>
        public IEnumerable<ExprNode> Nodes => Kind == RegionKind.Template
            ? Template.Expressions.Select(s => s.Node).Where(n => n != null)
            : Parse.Nodes;
    }
}
