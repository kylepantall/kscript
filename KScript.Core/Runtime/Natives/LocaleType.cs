using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>locale</c>: a BCP 47 language tag such as en-GB. Tags sort alphabetically, which keeps a language's regions
    /// together (en, en-GB, en-US); there is no arithmetic on them.
    /// </summary>
    public sealed class LocaleType : NativeType
    {
        public override string Name => "locale";

        public override string Description => "A language tag (BCP 47) such as en, en-GB or pt-BR, in its usual case.";

        public override string Example => "en-GB";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable.Concat(new[] { "split" }).ToArray();

        public override IReadOnlyList<string> Completions => new[] { "en", "en-GB", "en-US", "fr", "fr-FR", "de", "de-DE", "es", "pt-BR", "ja" };

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String;
            const string Let = "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n";
            m.Field("language", S, "The language code: en in en-GB.", (t, _) => ((NativeText)t).Part("language"), Let + "<echo>{ $x.language }</echo>");
            m.Field("region", S, "The region code: GB in en-GB, or '' when there isn't one.", (t, _) => ((NativeText)t).Part("region"), Let + "<echo>{ $x.region }</echo>");
            m.Field("name", S, "Its name in English: English (United Kingdom).", (t, _) => ((NativeText)t).Part("name"), Let + "<echo>{ $x.name }</echo>");
            m.Field("native_name", S, "Its name in its own language: français (France).", (t, _) => ((NativeText)t).Part("native_name"), Let + "<echo>{ $x.native_name }</echo>");
            m.Method("format", "value: any", "A number or date written the way this locale writes it: 1.234,5 in de-DE.", S, (t, c) =>
            {
                CultureInfo culture = (CultureInfo)((NativeText)t).Part("culture");
                return c.Arg(0) switch
                {
                    double d => d.ToString("#,0.##########", culture),
                    DateTime dt => dt.ToString("d", culture),
                    object o => Values.ToStr(o),
                    null => string.Empty,
                };
            }, Let + "<echo>{ $x->format(1234.5) }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string t = s.Trim().Replace('_', '-');
            if (t.Length == 0) return (null, "it is empty");
            string[] parts = t.Split('-');
            if (parts[0].Length is < 2 or > 3 || !parts[0].All(char.IsAsciiLetter)) return (null, "it must start with a two- or three-letter language code, as in en or en-GB");
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(t, predefinedOnly: true);
            }
            catch (CultureNotFoundException)
            {
                return (null, "it isn't a known language tag");
            }
            string tag = string.Join("-", parts.Select((p, i) => i == 0 ? p.ToLowerInvariant() : p.Length == 2 ? p.ToUpperInvariant() : p.Length == 4 ? char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant() : p.ToLowerInvariant()));
            string region = parts.Skip(1).FirstOrDefault(p => p.Length == 2 || (p.Length == 3 && p.All(char.IsAsciiDigit)))?.ToUpperInvariant() ?? string.Empty;
            return (new NativeText("locale", tag, new Dictionary<string, object>
            {
                ["language"] = parts[0].ToLowerInvariant(),
                ["region"] = region,
                ["name"] = culture.EnglishName,
                ["native_name"] = culture.NativeName,
                ["culture"] = culture,
            }), null);
        }
    }
}
