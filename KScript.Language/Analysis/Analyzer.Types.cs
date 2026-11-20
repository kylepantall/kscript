using KScript.Language.Catalog;
using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Types that flow between declarations: what functions return (their own and imported ones), assignments that
    /// fill in a variable declared as null, and parameter types taken from their defaults.
    /// </summary>
    public sealed partial class Analyzer
    {
        /// <summary>The most passes <see cref="Analyze"/> makes to settle function return types.</summary>
        private const int MaxPasses = 5;

        /// <summary>Return types from the previous pass, by the offset of the &lt;fn&gt; element.</summary>
        private IReadOnlyDictionary<int, TypeShape> knownReturns = new Dictionary<int, TypeShape>();

        /// <summary>Files being analysed for their exports on this thread, so import cycles stop.</summary>
        [ThreadStatic]
        private static HashSet<string> analysingImports;

        /// <summary>The exports of imported files, by path, shared by the passes of one analysis.</summary>
        private Dictionary<string, Dictionary<string, (SymbolKind Kind, TypeShape Shape)>> importCache =
            new Dictionary<string, Dictionary<string, (SymbolKind, TypeShape)>>(StringComparer.Ordinal);

        /// <summary>
        /// Runs the analysis until function return types stop changing. A function can be called above the line that
        /// declares it, so the first pass may not know what it returns yet; the next pass does.
        /// </summary>
        private static Analyzer RunToFixedPoint(Func<Analyzer> create)
        {
            Analyzer analyzer = create();
            analyzer.Run();
            for (int pass = 1; pass < MaxPasses; pass++)
            {
                Dictionary<int, TypeShape> returns = analyzer.CollectReturnShapes();
                if (returns.Count == 0 || SameReturns(returns, analyzer.knownReturns))
                {
                    break;
                }
                Analyzer next = create();
                next.knownReturns = returns;
                next.importCache = analyzer.importCache;
                next.Run();
                analyzer = next;
            }
            return analyzer;
        }

        private static bool SameReturns(IReadOnlyDictionary<int, TypeShape> a, IReadOnlyDictionary<int, TypeShape> b) =>
            a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out TypeShape other) && other.Display() == kv.Value.Display());

        private Dictionary<int, TypeShape> CollectReturnShapes() =>
            result.Symbols.Where(s => s.Kind == SymbolKind.Function && s.SourcePath == null && s.DeclaringElement != null && s.ReturnShape.Type != KType.Any)
                  .GroupBy(s => s.DeclaringElement.Span.Start)
                  .ToDictionary(g => g.Key, g => g.First().ReturnShape);

        /// <summary>Gives hoisted functions the return types found by the last pass, and imported names their types.</summary>
        private void ApplyKnownTypes(Scope root)
        {
            foreach ((Symbol symbol, int _) in root.Symbols)
            {
                if (symbol.SourcePath == null)
                {
                    if (symbol.Kind == SymbolKind.Function && symbol.DeclaringElement != null && knownReturns.TryGetValue(symbol.DeclaringElement.Span.Start, out TypeShape shape))
                    {
                        symbol.ReturnShape = shape;
                    }
                    continue;
                }

                Dictionary<string, (SymbolKind Kind, TypeShape Shape)> exports = ImportedTypes(symbol.SourcePath);
                if (exports == null || !exports.TryGetValue(symbol.Name, out var export))
                {
                    continue;
                }
                if (symbol.Kind == SymbolKind.Function && export.Kind == SymbolKind.Function)
                {
                    symbol.ReturnShape = export.Shape;
                }
                else if (symbol.Kind != SymbolKind.Function && export.Kind != SymbolKind.Function && symbol.Shape.Type == KType.Any)
                {
                    symbol.Shape = export.Shape;
                }
            }
        }

        /// <summary>The return types of an imported file's functions and the types of its top-level variables.</summary>
        private Dictionary<string, (SymbolKind Kind, TypeShape Shape)> ImportedTypes(string path)
        {
            if (importCache.TryGetValue(path, out var cached))
            {
                return cached;
            }
            string text = files.ReadText(path);
            if (text == null)
            {
                return null;
            }

            analysingImports ??= new HashSet<string>(StringComparer.Ordinal);
            if (!analysingImports.Add(path))
            {
                return null;
            }
            try
            {
                DocumentAnalysis other = Analyze(text, path, files, catalog);
                Dictionary<string, (SymbolKind, TypeShape)> exports = new Dictionary<string, (SymbolKind, TypeShape)>(StringComparer.Ordinal);
                // Top-level names live in the scope of the <kscript> element's content.
                HashSet<Scope> topLevel = new HashSet<Scope>(other.RootScope.Children.Where(c => other.Markup.RootElements.Any(r => r.ContentSpan.Equals(c.Span)))) { other.RootScope };
                foreach (Symbol s in other.Symbols.Where(s => s.SourcePath == null && s.DeclaringElement != null))
                {
                    if (s.Kind == SymbolKind.Function)
                    {
                        exports.TryAdd(s.Name, (SymbolKind.Function, s.ReturnShape));
                    }
                    else if ((s.Kind == SymbolKind.Constant || s.Kind == SymbolKind.Variable) && topLevel.Any(scope => scope.Symbols.Any(r => r.Symbol == s)))
                    {
                        exports.TryAdd(s.Name, (s.Kind, s.Shape));
                    }
                }
                importCache[path] = exports;
                return exports;
            }
            finally
            {
                analysingImports.Remove(path);
            }
        }

        /// <summary>After a &lt;fn&gt; is walked: works out what it returns from value="..." or its &lt;return&gt; elements.</summary>
        private void RecordReturnShape(MarkupElement fn)
        {
            Symbol symbol = result.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Function && s.DeclaringElement == fn && s.SourcePath == null);
            if (symbol == null)
            {
                return;
            }

            List<TypeShape> shapes = new List<TypeShape>();
            foreach (ExpressionRegion region in result.Regions)
            {
                if (region.Attribute?.Name != "value" || region.Parse?.Root == null)
                {
                    continue;
                }
                bool own = region.Element == fn
                    || (region.Element.Name == "return" && region.Element.Ancestors().FirstOrDefault(a => IsKScriptElementName(a, "fn")) == fn);
                if (own)
                {
                    shapes.Add(Infer(region.Parse.Root, region));
                }
            }

            TypeShape returned = Unify(shapes);
            // A recursive function's own call gives nothing; keep what the last pass found rather than forgetting it.
            if (returned.Type != KType.Any || symbol.ReturnShape.Type == KType.Any)
            {
                symbol.ReturnShape = returned;
            }
        }

        /// <summary>One shape for several values: null and unknown values don't change it; different types give any.</summary>
        internal static TypeShape Unify(IEnumerable<TypeShape> shapes)
        {
            List<TypeShape> known = shapes.Where(s => s != null && !s.IsVague).ToList();
            if (known.Count == 0)
            {
                return shapes.Any(s => s?.Type == KType.Null) ? new TypeShape(KType.Null) : TypeShape.Unknown;
            }
            TypeShape first = known[0];
            if (known.Any(s => s.Type != first.Type))
            {
                return TypeShape.Unknown;
            }
            if (first.Type != KType.Map || known.Count == 1)
            {
                return first;
            }
            TypeShape merged = new TypeShape(KType.Map);
            foreach (TypeShape s in known)
            {
                foreach (KeyValuePair<string, FieldInfo> f in s.Fields)
                {
                    merged.Fields.TryAdd(f.Key, f.Value);
                }
            }
            return merged;
        }

        /// <summary>
        /// <c>&lt;let name="x" value="null" /&gt;</c> then <c>&lt;set name="x" value="'a'" /&gt;</c>: $x holds text, so it
        /// gets string's methods. Only variables whose type isn't known yet are filled in.
        /// </summary>
        private static void Widen(Symbol symbol, TypeShape assigned)
        {
            if (symbol == null || assigned == null || assigned.IsVague || symbol.Kind == SymbolKind.Function || symbol.Kind == SymbolKind.Import || symbol.Kind == SymbolKind.BuiltIn)
            {
                return;
            }
            if (symbol.Shape.IsVague)
            {
                symbol.Shape = assigned;
            }
        }

        /// <summary>True for the text of an element with format="json" that is already JSON (the runtime uses it as written).</summary>
        private static bool IsLiteralJson(MarkupElement e, MarkupText text)
        {
            if (e.GetAttributeValue("format")?.Trim() != "json")
            {
                return false;
            }
            string value = text.Value.Trim();
            if (value.Length == 0 || (value[0] != '{' && value[0] != '['))
            {
                return false;
            }
            try
            {
                using System.Text.Json.JsonDocument _ = System.Text.Json.JsonDocument.Parse(value);
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }

        /// <summary>The type of a parameter: its declared type, or the type of a literal default (<c>count = 0</c>).</summary>
        private static TypeShape ParameterShape(FunctionParameter p)
        {
            if (p.Type != null)
            {
                return new TypeShape(p.Type == "int" || p.Type == "float" ? KType.Number : p.Type);
            }
            if (p.Default == null)
            {
                return TypeShape.Unknown;
            }
            ExprNode value = ExpressionParser.ParseExpression(p.Default).Root;
            return value switch
            {
                LiteralNode { Kind: LiteralKind.Number } => new TypeShape(KType.Number),
                LiteralNode { Kind: LiteralKind.String } => new TypeShape(KType.String),
                LiteralNode { Kind: LiteralKind.Bool } => new TypeShape(KType.Bool),
                ListNode => new TypeShape(KType.List),
                MapNode => new TypeShape(KType.Map),
                LambdaNode => new TypeShape(KType.Fn),
                _ => TypeShape.Unknown,
            };
        }
    }
}
