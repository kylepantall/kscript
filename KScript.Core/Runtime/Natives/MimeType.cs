using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>mime</c>: a media type such as text/html or application/json, lowercased, with any parameters. Media types
    /// sort alphabetically, which groups them by their main type (application/..., image/..., text/...).
    /// </summary>
    public sealed class MimeType : NativeType
    {
        public override string Name => "mime";

        public override string Description => "A media type such as text/html or application/json, lowercased, with any parameters.";

        public override string Example => "application/json";

        public override RawText RawText => RawText.Word;

        public override string JsonFormat => "media-type";

        public override IReadOnlyList<string> StringMethods { get; } = NativeTypes.Searchable.Concat(new[] { "split" }).ToArray();

        public override IReadOnlyList<string> Completions => new[] { "application/json", "text/html", "text/plain", "text/csv", "image/png", "application/octet-stream" };

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String;
            const string Let = "<let name=\"x\" type=\"mime\" value=\"'text/html; charset=utf-8'\" />\n";
            m.Field("main_type", S, "The part before /: text in text/html. (->type() is mime, as for any value.)", (t, _) => Part(t, "type"), Let + "<echo>{ $x.main_type }</echo>");
            m.Field("subtype", S, "The part after /: html in text/html.", (t, _) => Part(t, "subtype"), Let + "<echo>{ $x.subtype }</echo>");
            m.Field("essence", S, "The type and subtype without parameters: text/html.", (t, _) => Part(t, "essence"), Let + "<echo>{ $x.essence }</echo>");
            m.Field("params", KType.Map, "The parameters as a map: { charset: 'utf-8' }.", (t, _) => new Dictionary<string, object>((Dictionary<string, object>)Part(t, "params")),
                Let + "<echo>{ $x.params }</echo>");
            m.Method("is", "pattern: string", "True when it matches text/html, text/* or */*.", KType.Bool, (t, c) =>
            {
                string[] want = c.Str(0).Trim().ToLowerInvariant().Split('/');
                return want.Length == 2 && (want[0] == "*" || want[0] == (string)Part(t, "type")) && (want[1] == "*" || want[1] == (string)Part(t, "subtype"));
            }, Let + "<echo>{ $x->is('text/*') }</echo>");
        }

        private static object Part(object target, string name) => ((NativeText)target).Part(name);

        public override (object Value, string Reason) Parse(string s)
        {
            s = s.Trim();
            string main = s.Split(';')[0].Trim().ToLowerInvariant();
            int slash = main.IndexOf('/');
            if (slash <= 0 || slash == main.Length - 1) return (null, "it needs a type and a subtype, as in text/html");
            bool Token(string t) => t.Length > 0 && t.All(c => char.IsAsciiLetterOrDigit(c) || "!#$&-^_.+".Contains(c));
            string type = main.Substring(0, slash), sub = main.Substring(slash + 1);
            if (!Token(type) || !Token(sub)) return (null, "the type and subtype can only use letters, digits and !#$&-^_.+");
            var parameters = new Dictionary<string, object>();
            var text = new StringBuilder(main);
            foreach (string p in s.Split(';').Skip(1).Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                int eq = p.IndexOf('=');
                if (eq <= 0) return (null, $"'{p}' needs a name and a value, as in charset=utf-8");
                string name = p.Substring(0, eq).Trim().ToLowerInvariant(), value = p.Substring(eq + 1).Trim().Trim('"');
                parameters[name] = value;
                text.Append("; ").Append(name).Append('=').Append(value);
            }
            return (new NativeText("mime", text.ToString(), new Dictionary<string, object>
            {
                ["type"] = type,
                ["subtype"] = sub,
                ["essence"] = main,
                ["params"] = parameters,
            }), null);
        }
    }
}
