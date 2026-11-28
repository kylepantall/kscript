using System.Security.Cryptography;
using System.Text;

using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Runtime.Dependencies
{
    /// <summary>
    /// A file's fingerprints, so a later run can tell exactly which parts of it changed. A script has one fingerprint
    /// per top-level declaration (<c>fn greet</c>, <c>model User</c>, <c>type Repo</c>...), one for each test, and one
    /// for the rest: its imports, constants and top-level code. Any other file has one fingerprint for its whole text.
    /// </summary>
    public sealed class SourceFingerprint
    {
        /// <summary>The key of the part of a file outside its declarations (or the whole of a file that isn't a script).</summary>
        public const string Rest = "*";

        /// <summary>The declarations fingerprinted on their own. extend blocks stay in the rest, since they change values they never name.</summary>
        public static readonly IReadOnlyCollection<string> DeclarationKinds = new HashSet<string>(StringComparer.Ordinal) { "fn", "enum", "type", "error", "model", "job" };

        /// <summary>The fingerprint of everything outside the declarations and tests.</summary>
        public string RestHash { get; init; }

        /// <summary>Each declaration's fingerprint, by <see cref="KeyOf"/>.</summary>
        public IReadOnlyDictionary<string, string> Declarations { get; init; } = new Dictionary<string, string>();

        /// <summary>The fingerprints of the file's test blocks, in the order they are written.</summary>
        public IReadOnlyList<string> Tests { get; init; } = Array.Empty<string>();

        /// <summary>The fingerprint of one part (<see cref="Rest"/> or a declaration key), or null when the file has no such part.</summary>
        public string Of(string key) => key == Rest ? RestHash : Declarations.TryGetValue(key, out string hash) ? hash : null;

        /// <summary>True for a .kscript or .ks.xml file.</summary>
        public static bool IsScript(string path) =>
            path != null && (path.EndsWith(".kscript", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ks.xml", StringComparison.OrdinalIgnoreCase));

        /// <summary>The fingerprints of a file on disk, or null when it doesn't exist or can't be read.</summary>
        public static SourceFingerprint ForFile(string path)
        {
            string text;
            try
            {
                if (!File.Exists(path)) return null;
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
            return Of(path, text);
        }

        /// <summary>The fingerprints of a file's text.</summary>
        public static SourceFingerprint Of(string path, string text)
        {
            if (!IsScript(path) && !(path != null && TestFileName(path))) return new SourceFingerprint { RestHash = Hash(text) };

            MarkupDocument document;
            try
            {
                document = MarkupParser.Parse(new SourceText(text), DocumentKind.Script, path);
            }
            catch (Exception)
            {
                return new SourceFingerprint { RestHash = Hash(text) };
            }
            MarkupElement root = document.RootElements.FirstOrDefault();
            if (root == null || document.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                // A file that doesn't parse can't be split up, so any change to it counts as a change to all of it.
                return new SourceFingerprint { RestHash = Hash(text) };
            }

            var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
            var cut = new List<TextSpan>();
            foreach ((string key, MarkupElement e, IReadOnlyList<MarkupElement> methods) in DeclarationsOf(root))
            {
                // A type's methods are fingerprinted on their own, so changing one only affects the code that calls it.
                string hash = Hash(Without(text, e.Span, methods.Select(m => m.Span).ToList()));
                // Two declarations with one key (a mistake the checker reports) share it: either changing counts.
                declarations[key] = declarations.TryGetValue(key, out string earlier) ? Hash(earlier + hash) : hash;
                if (e.Parent == root) cut.Add(e.Span);
            }
            var tests = new List<string>();
            var testSpans = new List<TextSpan>();
            foreach (MarkupElement test in TestBlocks(root))
            {
                tests.Add(Hash(Slice(text, test.Span)));
                testSpans.Add(test.Span);
            }
            // Tests leave no marker: adding one is seen in Tests, and shouldn't count as a change to the file's other code.
            string rest = Without(text, new TextSpan(0, text.Length), cut);
            if (testSpans.Count > 0) rest = Without(rest, new TextSpan(0, rest.Length), Shift(testSpans, cut), marker: false);
            return new SourceFingerprint { RestHash = Hash(rest), Declarations = declarations, Tests = tests };
        }

        /// <summary>The test blocks of a file, outside any declaration.</summary>
        public static IEnumerable<MarkupElement> TestBlocks(MarkupElement root)
        {
            var pending = new Stack<MarkupElement>();
            foreach (MarkupElement e in root.ChildElements.Reverse()) pending.Push(e);
            while (pending.Count > 0)
            {
                MarkupElement e = pending.Pop();
                if (e.Name == "test")
                {
                    yield return e;
                    continue;
                }
                if (KeyOf(e) != null) continue;
                foreach (MarkupElement child in e.ChildElements.Reverse()) pending.Push(child);
            }
        }

        /// <summary>The fingerprint of a test block's text.</summary>
        public static string TestHash(MarkupElement test, SourceText source) => Hash(Slice(source.Text, test.Span));

        /// <summary>
        /// The key a top-level declaration is fingerprinted under, such as <c>fn:greet</c> or <c>model:User</c>, or null
        /// for anything else (imports, constants, statements, extend blocks).
        /// </summary>
        public static string KeyOf(MarkupElement e)
        {
            if (!DeclarationKinds.Contains(e.Name)) return null;
            string name = e.GetAttributeValue("name")?.Trim().TrimStart('@', '$');
            return string.IsNullOrEmpty(name) ? null : e.Name + ":" + name;
        }

        /// <summary>
        /// The declarations of a script, each with its key: the top-level ones (<c>type:User</c>), and the methods of
        /// types, models, errors and jobs (<c>type:User/fn:shout</c>), each with the methods held out of it.
        /// </summary>
        public static IEnumerable<(string Key, MarkupElement Element, IReadOnlyList<MarkupElement> Methods)> DeclarationsOf(MarkupElement root)
        {
            foreach (MarkupElement e in root.ChildElements)
            {
                if (KeyOf(e) is not string key) continue;
                List<MarkupElement> methods = e.Name == "fn" ? new List<MarkupElement>() : e.ChildElements.Where(m => m.Name == "fn" && KeyOf(m) != null).ToList();
                yield return (key, e, methods);
                foreach (MarkupElement method in methods) yield return (key + "/" + KeyOf(method), method, Array.Empty<MarkupElement>());
            }
        }

        /// <summary>A short, stable fingerprint of some text.</summary>
        public static string Hash(string text)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return Convert.ToHexString(digest, 0, 12).ToLowerInvariant();
        }

        private static bool TestFileName(string path) => path.EndsWith(".test.xml", StringComparison.OrdinalIgnoreCase);

        private static string Slice(string text, TextSpan span)
        {
            int start = Math.Clamp(span.Start, 0, text.Length);
            return text.Substring(start, Math.Clamp(span.Length, 0, text.Length - start));
        }

        /// <summary>Test spans moved to where they are once the declarations (none of which holds a test) are cut to one marker each.</summary>
        private static List<TextSpan> Shift(List<TextSpan> tests, List<TextSpan> cut)
        {
            var shifted = new List<TextSpan>();
            foreach (TextSpan test in tests)
            {
                int before = cut.Where(c => c.End <= test.Start).Sum(c => c.Length - 1);
                shifted.Add(new TextSpan(test.Start - before, test.Length));
            }
            return shifted;
        }

        /// <summary>The text of <paramref name="region"/> with each of <paramref name="spans"/> (inside it) replaced by a marker.</summary>
        private static string Without(string text, TextSpan region, List<TextSpan> spans, bool marker = true)
        {
            int end = Math.Clamp(region.End, 0, text.Length);
            var rest = new StringBuilder(end);
            int at = Math.Clamp(region.Start, 0, text.Length);
            foreach (TextSpan span in spans.OrderBy(s => s.Start))
            {
                int start = Math.Clamp(span.Start, at, end);
                // A test goes with the blank lines before it, so adding or removing one leaves the rest as it was.
                while (!marker && start > at && char.IsWhiteSpace(text[start - 1])) start--;
                if (start > at) rest.Append(text, at, start - at);
                // Where a declaration was, so adding, removing or moving one still counts as a change.
                if (marker) rest.Append('\u0001');
                at = Math.Max(at, Math.Clamp(span.End, 0, end));
            }
            if (at < end) rest.Append(text, at, end - at);
            return rest.ToString();
        }
    }
}
