using KScript.Syntax.Markup;
using KScript.Syntax.Text;
using KScript.Syntax;

namespace KScript.Language.Analysis
{
    /// <summary>
    /// Types for parameters that don't say theirs, taken from the calls in the same file: when every call to
    /// <c>@cached('a', fn() => 1)</c> passes text first and a function second, <c>key</c> is a string and
    /// <c>compute</c> a fn. A parameter some call passes something unknown to, or values of different types, gets none.
    /// </summary>
    public sealed partial class Analyzer
    {
        /// <summary>What each call passed, by the offset of the &lt;fn&gt; element and the parameter's name.</summary>
        private readonly Dictionary<(int Fn, string Name), List<TypeShape>> callArguments = new Dictionary<(int, string), List<TypeShape>>();

        /// <summary>Parameter types the previous pass found from the calls, by the offset of the &lt;fn&gt; element and the parameter's name.</summary>
        private IReadOnlyDictionary<(int Fn, string Name), TypeShape> knownParameters = new Dictionary<(int, string), TypeShape>();

        /// <summary>Notes the types a call to one of this file's functions passes, by parameter.</summary>
        private void NoteCallArguments(Symbol fn, IReadOnlyList<ArgumentNode> args, TypeShape piped, ExpressionRegion region)
        {
            if (fn.SourcePath != null || fn.DeclaringElement == null)
            {
                return;
            }
            int position = 0;
            if (piped != null)
            {
                Note(fn, position++, null, piped);
            }
            foreach (ArgumentNode a in args)
            {
                Note(fn, a.Name == null ? position++ : -1, a.Name, Infer(a.Value, region));
            }
        }

        private void Note(Symbol fn, int index, string name, TypeShape shape)
        {
            string parameter = name ?? (index < fn.Parameters.Count ? fn.Parameters[index].Name : null);
            if (parameter == null)
            {
                return;
            }
            (int, string) key = (fn.DeclaringElement.Span.Start, parameter);
            if (!callArguments.TryGetValue(key, out List<TypeShape> shapes))
            {
                callArguments[key] = shapes = new List<TypeShape>();
            }
            shapes.Add(shape);
        }

        /// <summary>The parameter types every call agrees on.</summary>
        private Dictionary<(int Fn, string Name), TypeShape> CollectParameterShapes()
        {
            Dictionary<(int, string), TypeShape> found = new Dictionary<(int, string), TypeShape>();
            foreach (KeyValuePair<(int Fn, string Name), List<TypeShape>> kv in callArguments)
            {
                if (kv.Value.Any(s => s.IsVague))
                {
                    continue;
                }
                TypeShape shape = Unify(kv.Value);
                if (!shape.IsVague)
                {
                    found[kv.Key] = kv.Value.All(s => s.Display() == shape.Display()) ? shape : new TypeShape(shape.Type);
                }
            }
            return found;
        }

        private static bool SameParameters(IReadOnlyDictionary<(int, string), TypeShape> a, IReadOnlyDictionary<(int, string), TypeShape> b) =>
            a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out TypeShape other) && other.Display() == kv.Value.Display());

        /// <summary>The type the calls give a parameter with no type or default of its own, or null.</summary>
        private TypeShape InferredParameterShape(FunctionParameter p, int fnOffset) =>
            p.Type == null && p.Default == null && knownParameters.TryGetValue((fnOffset, p.Name), out TypeShape shape) ? shape : null;
        /// <summary>
        /// KS6007: a function parameter with no type (and no default to take one from). A type tells callers and the
        /// editor what it takes. When every call in the file agrees, the quick fix writes that type in.
        /// </summary>
        private void CheckParameterTypes(MarkupElement fn, List<FunctionParameter> parameters, List<Symbol> declared)
        {
            // A job's functions are hooks the queue calls (failed(err) gets an error, which has no type to write).
            if (Kind != DocumentKind.Script || fn.Parent?.Name == "job")
            {
                return;
            }
            for (int i = 0; i < parameters.Count; i++)
            {
                FunctionParameter p = parameters[i];
                if (p.Type != null || p.Default != null || p.Span.Length == 0)
                {
                    continue;
                }
                string written = declared[i].TypeFromCalls ? WrittenType(declared[i].Shape) : null;
                QuickFix[] fix = written != null ? new[] { new QuickFix($"Write {p.Name}: {written}", new TextChange(new TextSpan(p.Span.End, 0), ": " + written)) } : null;
                string message = written != null
                    ? $"Give {p.Name} a type: every call passes {(written.EndsWith("[]", StringComparison.Ordinal) ? "a list" : written == "fn" ? "a function" : Article(written))}, so write {p.Name}: {written}."
                    : $"Give {p.Name} a type, such as {p.Name}: string or {p.Name}: map, so callers and the editor know what it takes.";
                Report(p.Span, DiagnosticSeverity.Warning, DiagnosticCodes.MissingParameterType, message, fix);
            }
        }

        /// <summary>How a shape is written as a parameter's type (<c>string</c>, <c>User</c>, <c>int[]</c>), or null when it can't be.</summary>
        internal static string WrittenType(TypeShape shape)
        {
            if (shape.Name != null)
            {
                return shape.Type is KType.Type or KType.Query ? null : shape.Name;
            }
            if (shape.Type == KType.List)
            {
                return shape.Element != null && WrittenType(shape.Element) is string item && !item.EndsWith("[]", StringComparison.Ordinal) ? item + "[]" : "list";
            }
            return IsBuiltInTypeName(shape.Type) && shape.Type != KType.Any ? shape.Type : null;
        }
    }
}
