using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>hex</c>: hexadecimal digits, such as a hash (a3f9c0), lowercased with any 0x dropped. Hex values sort by
    /// their text, which for values of the same length (hashes, ids) is also their numeric order. Leading zeros are
    /// kept and count, because a hex value is usually bytes rather than a number.
    /// </summary>
    public sealed class HexType : NativeType
    {
        public override string Name => "hex";

        public override string Description => "Hexadecimal digits, such as a hash: a3f9c0. Lowercased; 0x is dropped.";

        public override string Example => "a3f9c0";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> Options => new[] { "bytes" };

        public override IReadOnlyList<string> StringMethods { get; } = NativeTypes.Searchable.Concat(new[] { "upper" }).ToArray();

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string Let = "<let name=\"x\" type=\"hex\" value=\"'0xFF80'\" />\n";
            m.Field("bytes", KType.Number, "How many bytes it holds: half its number of digits.", (t, _) => Math.Ceiling(((NativeText)t).Text.Length / 2.0),
                Let + "<echo>{ $x.bytes }</echo>");
            m.Method("to_number", "", "Its value as a number: 'ff' gives 255.", KType.Number, (t, c) =>
            {
                string text = ((NativeText)t).Text;
                if (text.Length > 13) throw new KScriptException("A hex value with more than 13 digits is too big to be a number.", "Value");
                return (double)long.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }, Let + "<echo>{ $x->to_number() }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            if (s.Length == 0) return (null, "it is empty");
            char bad = s.FirstOrDefault(c => !Uri.IsHexDigit(c));
            if (bad != default) return (null, $"'{bad}' isn't a hex digit (0-9, a-f)");
            return (new NativeText("hex", s.ToLowerInvariant()), null);
        }

        public override IEnumerable<string> OptionProblems(object value, IReadOnlyDictionary<string, object> options)
        {
            if (value is NativeText hex && options.TryGetValue("bytes", out object b) && Values.ToInt(b, "bytes") is int bytes && hex.Text.Length != bytes * 2)
                yield return $"must be {bytes} bytes ({bytes * 2} hex digits), but it has {hex.Text.Length} digits";
        }
    }
}
