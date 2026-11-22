using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>base64</c>: base64 text, standard or URL-safe, with or without = padding. Two values are == when they hold
    /// the same bytes, so aGk= == aGk and the URL-safe spelling equals the standard one. They have no order: sorting
    /// encoded bytes by their letters means nothing.
    /// </summary>
    public sealed class Base64Type : NativeType
    {
        public override string Name => "base64";

        public override string Description => "Base64 text (standard or URL-safe). ->decode() gives the text it holds.";

        public override string Example => "aGVsbG8=";

        public override RawText RawText => RawText.Word;

        public override string JsonFormat => "byte";

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable;

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        // Padding and the URL-safe letters (- and _ for + and /) are how the bytes are spelled, not what they are.
        public override Equality Equality => Equality.By((a, b) => Bytes(((NativeText)a).Text).AsSpan().SequenceEqual(Bytes(((NativeText)b).Text)));

        public override Ordering Ordering => Ordering.None;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string Let = "<let name=\"x\" type=\"base64\" value=\"'aGVsbG8gd29ybGQ='\" />\n";
            m.Method("decode", "", "The text it holds (read as UTF-8).", KType.String, (t, c) => Encoding.UTF8.GetString(Bytes(((NativeText)t).Text)),
                Let + "<echo>{ $x->decode() }</echo>");
            m.Field("bytes", KType.Number, "How many bytes it holds.", (t, _) => (double)Bytes(((NativeText)t).Text).Length,
                Let + "<echo>{ $x.bytes }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            s = s.Trim();
            if (s.Length == 0) return (null, "it is empty");
            if (s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '-' or '_' or '='))) return (null, "it can only use letters, digits, + / - _ and =");
            try
            {
                Bytes(s);
            }
            catch (FormatException)
            {
                return (null, "it isn't complete base64 (check its length and = padding)");
            }
            return (new NativeText("base64", s), null);
        }

        /// <summary>The bytes base64 text holds, standard or URL-safe, padded or not. Throws FormatException when it isn't base64.</summary>
        internal static byte[] Bytes(string s)
        {
            string t = s.Trim().Replace('-', '+').Replace('_', '/');
            t = t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=');
            return Convert.FromBase64String(t);
        }
    }
}
