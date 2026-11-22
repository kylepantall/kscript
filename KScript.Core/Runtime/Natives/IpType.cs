using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>ip</c>: an IPv4 or IPv6 address, kept in its standard form. Addresses sort by their numeric value, every
    /// IPv4 address before every IPv6 one (the order ping tables and Python's ipaddress module use), so 10.0.0.9
    /// comes before 10.0.0.10. A number moves an address along (<c>$ip + 1</c>), two addresses give the distance
    /// between them, and <c>$a..$b</c> walks the addresses in between.
    /// </summary>
    public sealed class IpType : NativeType
    {
        public override string Name => "ip";

        public override string Description => "An IPv4 or IPv6 address, written the standard way: 192.168.0.1 or 2001:db8::1.";

        public override string Example => "192.168.0.1";

        public override RawText RawText => RawText.Word;

        public override string Article => "an";

        public override string JsonFormat => "ip";

        public override IReadOnlyList<string> Options => new[] { "version" };

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable.Concat(new[] { "split" }).ToArray();

        public override IReadOnlyList<string> Completions => new[] { "127.0.0.1", "0.0.0.0", "192.168.0.1", "::1" };

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.By(CompareAddresses);

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
            const string I = "ip", N = "number";
            o.Binary("+", I, N, I, "The address this many places after: 10.0.0.1 + 5 is 10.0.0.6.", (a, b) => Move(a, Whole(b, "ip +")))
             .Binary("-", I, N, I, "The address this many places before: 10.0.0.6 - 5 is 10.0.0.1.", (a, b) => Move(a, -Whole(b, "ip -")))
             .Binary("-", I, I, N, "How many places the second address is before the first: 10.0.0.10 - 10.0.0.1 is 9.", (a, b) => (double)Distance(b, a))
             .Range(I, N, "The addresses from the start up to (not including) the end, a step apart: $first..$last step 2. Without a step, every address.", AddressRange);
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string B = KType.Bool, N = KType.Number;
            const string Let = "<let name=\"x\" type=\"ip\" value=\"'192.168.0.12'\" />\n";
            m.Field("version", N, "4 or 6.", (t, _) => ((NativeText)t).Part("version"), Let + "<echo>{ $x.version }</echo>");
            m.Method("in", "block: cidr", "True when the address is in the block, such as '10.0.0.0/8'.", B, (t, c) =>
                CidrType.Contains(c.Arg(0) is NativeText block && block.TypeName == "cidr" ? block : CidrType.Read(c.Str(0)), Address(t)),
                Let + "<echo>{ $x->in('192.168.0.0/16') }</echo>");
            m.Field("is_private", B, "True for addresses that only work inside a network: 10.x, 172.16-31.x, 192.168.x, fc00::/7, and loopback and link-local ones.", (t, _) =>
            {
                IPAddress ip = Address(t);
                if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal) return true;
                if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
                byte[] b = ip.GetAddressBytes();
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
            }, Let + "<echo>{ $x.is_private }</echo>");
            m.Field("is_loopback", B, "True for 127.x.x.x and ::1.", (t, _) => IPAddress.IsLoopback(Address(t)), Let + "<echo>{ $x.is_loopback }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            s = s.Trim();
            if (s.StartsWith('[') && s.EndsWith(']')) s = s.Substring(1, s.Length - 2);
            if (s.Length == 0) return (null, "it is empty");
            if (!IPAddress.TryParse(s, out IPAddress ip) || (ip.AddressFamily == AddressFamily.InterNetwork && s.Count(c => c == '.') != 3))
                return (null, "it isn't an IPv4 address (such as 192.168.0.1) or an IPv6 address (such as 2001:db8::1)");
            if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.ScopeId != 0) return (null, "an IPv6 zone (%...) can't be kept");
            return (Of(ip), null);
        }

        public override IEnumerable<string> OptionProblems(object value, IReadOnlyDictionary<string, object> options)
        {
            if (value is NativeText ip && options.TryGetValue("version", out object v) && Values.ToStr(v) is string want && want != "any"
                && want != ((double)ip.Part("version")).ToString(CultureInfo.InvariantCulture))
                yield return $"must be an IPv{want} address";
        }

        /// <summary>An ip value for an address.</summary>
        public static NativeText Of(IPAddress ip) =>
            new NativeText("ip", ip.ToString(), new Dictionary<string, object> { ["address"] = ip, ["version"] = ip.AddressFamily == AddressFamily.InterNetwork ? 4.0 : 6.0 });

        /// <summary>The address an ip value holds.</summary>
        public static IPAddress Address(object ip) => (IPAddress)((NativeText)ip).Part("address");

        /// <summary>An address as a whole number: 10.0.0.1 is 167772161.</summary>
        internal static BigInteger Number(IPAddress ip) => new BigInteger(ip.GetAddressBytes(), isUnsigned: true, isBigEndian: true);

        /// <summary>The address of this family that is this whole number, or an error when the number is out of its range.</summary>
        internal static IPAddress FromNumber(BigInteger n, AddressFamily family)
        {
            int size = family == AddressFamily.InterNetwork ? 4 : 16;
            if (n.Sign < 0 || n >= BigInteger.One << (size * 8))
                throw new KScriptException($"That goes past the {(size == 4 ? "IPv4" : "IPv6")} addresses: they run from {(size == 4 ? "0.0.0.0 to 255.255.255.255" : ":: to ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")}.", "Value");
            byte[] bytes = n.ToByteArray(isUnsigned: true, isBigEndian: true);
            byte[] full = new byte[size];
            Array.Copy(bytes, 0, full, size - bytes.Length, bytes.Length);
            return new IPAddress(full);
        }

        /// <summary>IPv4 before IPv6, then by numeric value.</summary>
        internal static int CompareAddresses(object a, object b)
        {
            IPAddress x = Address(a), y = Address(b);
            int family = (x.AddressFamily == AddressFamily.InterNetwork ? 4 : 6).CompareTo(y.AddressFamily == AddressFamily.InterNetwork ? 4 : 6);
            return family != 0 ? family : Number(x).CompareTo(Number(y));
        }

        private static BigInteger Whole(object n, string what)
        {
            double d = (double)n;
            if (double.IsNaN(d) || double.IsInfinity(d) || d != Math.Floor(d))
                throw new KScriptException($"An address moves by a whole number, so {what} {Values.ToStr(n)} doesn't work.", "Value");
            return new BigInteger(d);
        }

        private static NativeText Move(object ip, BigInteger by)
        {
            IPAddress address = Address(ip);
            return Of(FromNumber(Number(address) + by, address.AddressFamily));
        }

        /// <summary>How many places <paramref name="to"/> is after <paramref name="from"/>; an error across IPv4 and IPv6.</summary>
        private static BigInteger Distance(object from, object to)
        {
            IPAddress a = Address(from), b = Address(to);
            if (a.AddressFamily != b.AddressFamily)
                throw new KScriptException($"{to} and {from} aren't both IPv4 or both IPv6, so there is no distance between them.", "Value");
            return Number(b) - Number(a);
        }

        private static IEnumerable<object> AddressRange(object start, object end, object step)
        {
            BigInteger span = Distance(start, end);
            BigInteger by = step == null ? (span >= 0 ? BigInteger.One : BigInteger.MinusOne) : Whole(step, "a step of");
            if (by.IsZero) throw new KScriptException("A range can't have a step of 0.", "Argument");
            for (BigInteger at = 0; by > 0 ? at < span : at > span; at += by) yield return Move(start, at);
        }
    }
}
