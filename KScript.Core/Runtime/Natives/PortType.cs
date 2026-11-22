using System.Globalization;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>port</c>: a network port from 1 to 65535. Its values are plain numbers (8080, not text), so arithmetic,
    /// comparisons and truthiness are a number's: <c>$port + 1</c> is 8081 and <c>$port &lt; 1024</c> works. It
    /// declares no operators of its own, since the runtime never sees a port value that isn't a number.
    /// </summary>
    public sealed class PortType : NativeType
    {
        public override string Name => "port";

        public override string Description => "A network port, a whole number from 1 to 65535.";

        public override string Example => "8080";

        public override RawText RawText => RawText.Word;

        public override bool TakesNumbers => true;

        public override IReadOnlyList<string> StringMethods => Array.Empty<string>();

        public override IReadOnlyList<string> Completions => new[] { "80", "443", "8080", "3000", "5432" };

        public override Equality Equality => Equality.By((a, b) => (double)a == (double)b);

        public override Ordering Ordering => Ordering.By((a, b) => ((double)a).CompareTo((double)b));

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
        }

        public override (object Value, string Reason) Parse(string s)
        {
            if (!int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port)) return (null, "it must be a whole number from 1 to 65535");
            if (port < 1 || port > 65535) return (null, "it must be from 1 to 65535");
            return ((double)port, null);
        }
    }
}
