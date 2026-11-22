using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>mac</c>: a network card's MAC address, kept as six lowercase pairs joined by colons. Every value has the same
    /// width and case, so sorting by text is sorting by the address's value. It takes no operators.
    /// </summary>
    public sealed class MacType : NativeType
    {
        public override string Name => "mac";

        public override string Description => "A network card's MAC address, written as six lowercase pairs: 00:1a:2b:3c:4d:5e.";

        public override string Example => "00:1a:2b:3c:4d:5e";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable.Concat(new[] { "split", "upper" }).ToArray();

        public override IReadOnlyList<string> Completions => new[] { "ff:ff:ff:ff:ff:ff" };

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
        }

        public override (object Value, string Reason) Parse(string s)
        {
            string hex = new string(s.Trim().Where(c => c is not (':' or '-' or '.')).ToArray()).ToLowerInvariant();
            if (hex.Length != 12 || !hex.All(Uri.IsHexDigit)) return (null, "it needs 12 hex digits, as in 00:1a:2b:3c:4d:5e");
            return (new NativeText("mac", string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)))), null);
        }
    }
}
