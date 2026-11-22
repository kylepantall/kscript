using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>domain</c>: a domain name such as api.example.com, lowercased and kept as it is written (bücher.example).
    /// Domains sort alphabetically and take no operators: ->is_subdomain_of() says whether one is under another.
    /// </summary>
    public sealed class DomainType : NativeType
    {
        public override string Name => "domain";

        public override string Description => "A domain name such as example.com or api.example.co.uk, lowercased, without a trailing dot.";

        public override string Example => "example.com";

        public override RawText RawText => RawText.Word;

        public override string JsonFormat => "hostname";

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable.Concat(new[] { "split", "upper" }).ToArray();

        public override IReadOnlyList<string> Completions => new[] { "example.com", "example.org" };

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String, B = KType.Bool, L = KType.List;
            const string Let = "<let name=\"x\" type=\"domain\" value=\"'api.example.co.uk'\" />\n";
            m.Field("tld", S, "The last part: com, or uk for example.co.uk.", (t, _) => ((NativeText)t).Part("tld"), Let + "<echo>{ $x.tld }</echo>");
            m.Field("labels", L, "Its parts: ['api', 'example', 'com'].", (t, _) => new List<object>((List<object>)((NativeText)t).Part("labels")), Let + "<echo>{ $x.labels }</echo>");
            m.Method("is_subdomain_of", "other: string", "True when it is the other domain or under it: api.example.com is under example.com.", B, (t, c) =>
            {
                string text = ((NativeText)t).Text;
                string other = c.Str(0).Trim().TrimEnd('.').ToLowerInvariant();
                try
                {
                    other = new IdnMapping().GetUnicode(other);
                }
                catch (ArgumentException)
                {
                    return false;
                }
                return text == other || text.EndsWith("." + other, StringComparison.Ordinal);
            }, Let + "<echo>{ $x->is_subdomain_of('example.co.uk') }</echo>");
            m.Method("ascii", "", "The form DNS uses, with international labels as xn--: xn--bcher-kva.example for bücher.example.", S, (t, c) => ((NativeText)t).Part("ascii"),
                "<let name=\"x\" type=\"domain\" value=\"'Bücher.example'\" />\n<echo>{ $x } { $x->ascii() }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string d = s.Trim().TrimEnd('.').ToLowerInvariant();
            try
            {
                d = new IdnMapping().GetAscii(d);
            }
            catch (ArgumentException)
            {
                return (null, "it isn't a valid domain name");
            }
            string problem = Problem(d);
            if (problem != null) return (null, problem);
            // Kept as it is written (bücher.example); ->ascii() gives the form DNS uses (xn--bcher-kva.example).
            string ascii = d;
            d = new IdnMapping().GetUnicode(ascii);
            string[] labels = d.Split('.');
            return (new NativeText("domain", d, new Dictionary<string, object>
            {
                ["tld"] = labels[^1],
                ["labels"] = labels.Cast<object>().ToList(),
                ["ascii"] = ascii,
            }), null);
        }

        private static string Problem(string d)
        {
            if (d.Length == 0) return "it is empty";
            if (d.Length > 253) return "it is longer than 253 characters";
            string[] labels = d.Split('.');
            if (labels.Length < 2) return "it needs a dot, as in example.com";
            foreach (string label in labels)
            {
                if (label.Length == 0) return "it has two dots in a row";
                if (label.Length > 63) return $"'{label}' is longer than 63 characters";
                if (label[0] == '-' || label[^1] == '-') return $"'{label}' can't start or end with -";
                char bad = label.FirstOrDefault(c => !(char.IsAsciiLetterOrDigit(c) || c == '-'));
                if (bad != default) return $"'{bad}' can't be used in a domain";
            }
            if (labels[^1].All(char.IsAsciiDigit)) return "its last part can't be a number (that is an IP address)";
            return null;
        }
    }
}
