using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>regex</c>: a regular expression written /.../ with flags, held as a <see cref="RegexValue"/>. Two are equal
    /// when they are written the same; they have no order and take no operators (->test() checks text against one).
    /// </summary>
    public sealed class RegexType : NativeType
    {
        public override string Name => KType.Regex;

        public override string Description => "A regular expression with flags i, m, s and x, written /.../ in code.";

        public override string Example => "/^[a-z]+$/i";

        public override RawText RawText => RawText.Word;

        public override string JsonFormat => "regex";

        public override IReadOnlyList<string> StringMethods => new[] { "contains", "starts_with", "ends_with", "count", "escape", "markdown_escape", "json" };

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.None;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String;
            static System.Text.RegularExpressions.Regex R(object t) => ((RegexValue)t).Regex;
            static object Safe(object t, Func<object> work) => RegexValue.Run((RegexValue)t, _ => work());
            m.Method("test", "text: string", "True when the text matches somewhere (use ^ and $ to match all of it).", KType.Bool, (t, c) => Safe(t, () => R(t).IsMatch(c.Str(0))),
                "/^[a-z]+$/i->test('Hello')");
            m.Method("match", "text: string", "The first match: { text, index, groups, captures }, where groups holds named groups (?<name>...) and captures numbered ones. null if none.", KType.Map,
                (t, c) => Safe(t, () => R(t).Match(c.Str(0)) is { Success: true } match ? RegexValue.MatchMap(R(t), match) : null),
                "/(\\d{4})-(\\d\\d)/->match('Due 2026-11').groups");
            m.Method("match_all", "text: string", "Every match, as { text, index, groups, captures } maps.", KType.List,
                (t, c) => Safe(t, () => R(t).Matches(c.Str(0)).Select(match => (object)RegexValue.MatchMap(R(t), match)).ToList()),
                "/\\d+/->match_all('3 cats, 12 dogs')->map(fn(m) => $m.text)");
            m.Method("replace", "text: string, with", "The text with every match replaced. with is text, where $1 and ${name} are groups, or fn(match) returning text.", S, (t, c) => Safe(t, () =>
                c.Arg(1) is ICallable f
                    ? R(t).Replace(c.Str(0), match => Values.ToStr(c.Call(f, RegexValue.MatchMap(R(t), match))))
                    : R(t).Replace(c.Str(0), c.Str(1))),
                "/(\\d{4})-(\\d\\d)/->replace('2026-11', '$2/$1')");
            m.Method("split", "text: string", "The text split at every match.", KType.List, (t, c) => Safe(t, () => R(t).Split(c.Str(0)).Select(x => (object)x).ToList()),
                "/\\s*[,;]\\s*/->split('tea, coffee;milk')");
            m.Field("pattern", S, "The pattern, without slashes or flags.", (t, _) => ((RegexValue)t).Pattern, "/^[a-z]+$/i.pattern");
            m.Field("flags", S, "The flags, such as i.", (t, _) => ((RegexValue)t).Flags, "/^[a-z]+$/i.flags");
        }

        public override (object Value, string Reason) Parse(string text) =>
            RegexValue.TryParse(text, out RegexValue regex, out string reason) ? (regex, null) : (null, reason);
    }
}
