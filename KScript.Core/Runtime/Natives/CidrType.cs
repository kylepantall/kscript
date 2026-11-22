using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>cidr</c>: a block of IP addresses such as 10.0.0.0/8, kept with its network address. <c>$ip in $block</c>
    /// says whether an address is in it, blocks sort by their network address and then their prefix, and a for loop
    /// walks the addresses of a block that holds no more than <see cref="MaxWalk"/>.
    /// </summary>
    public sealed class CidrType : NativeType
    {
        /// <summary>The most addresses a for loop walks: a /16 of IPv4. Bigger blocks are almost always a mistake.</summary>
        public const int MaxWalk = 65536;

        public override string Name => "cidr";

        public override string Description => "A block of IP addresses: an address, / and a prefix length, such as 10.0.0.0/8.";

        public override string Example => "10.0.0.0/8";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods => NativeTypes.Searchable.Concat(new[] { "split" }).ToArray();

        public override IReadOnlyList<string> Completions => new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "0.0.0.0/0" };

        public override Equality Equality => Equality.ByText;

        public override Ordering Ordering => Ordering.By((a, b) =>
        {
            int network = IpType.CompareAddresses(IpType.Of(Network(a)), IpType.Of(Network(b)));
            return network != 0 ? network : Prefix(a).CompareTo(Prefix(b));
        });

        public override Iteration Iteration => Iteration.Of("ip", Walk);

        protected override void DeclareOperators(NativeOperators o)
        {
            const string C = "cidr", I = "ip", B = "bool";
            o.Binary("in", I, C, B, "True when the address is in the block: 10.1.2.3 in 10.0.0.0/8 is true.", (a, b) => Contains((NativeText)b, IpType.Address(a)))
             .Binary("in", C, C, B, "True when the first block lies inside the second: 10.1.0.0/16 in 10.0.0.0/8 is true.", (a, b) =>
                 Prefix(a) >= Prefix(b) && Contains((NativeText)b, Network(a)));
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string B = KType.Bool, N = KType.Number;
            const string Let = "<let name=\"x\" type=\"cidr\" value=\"'10.0.0.0/8'\" />\n";
            m.Method("contains", "address: ip", "True when the address is in the block.", B, (t, c) =>
                Contains((NativeText)t, IpType.Address(c.Arg(0) is NativeText ip && ip.TypeName == "ip" ? ip : ReadIp(c.Str(0)))),
                Let + "<echo>{ $x->contains('10.20.30.40') }</echo>");
            m.Field("network", "ip", "The first address of the block.", (t, _) => IpType.Of(Network(t)), Let + "<echo>{ $x.network }</echo>");
            m.Field("last", "ip", "The last address of the block: 10.255.255.255 for 10.0.0.0/8.", (t, _) => IpType.Of(Last(t)), Let + "<echo>{ $x.last }</echo>");
            m.Field("prefix", N, "The prefix length: 8 for 10.0.0.0/8.", (t, _) => ((NativeText)t).Part("prefix"), Let + "<echo>{ $x.prefix }</echo>");
            m.Field("size", N, "How many addresses the block holds: 256 for a /24.", (t, _) => Math.Pow(2, Bits(t) - Prefix(t)), Let + "<echo>{ $x.size }</echo>");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            s = s.Trim();
            int slash = s.IndexOf('/');
            if (slash < 0) return (null, "it needs / and a prefix length, as in 10.0.0.0/8");
            if (new IpType().Parse(s.Substring(0, slash)) is not (NativeText address, null)) return (null, $"'{s.Substring(0, slash)}' isn't an IP address");
            IPAddress ip = IpType.Address(address);
            int bits = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            if (!int.TryParse(s.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int prefix) || prefix > bits)
                return (null, $"the prefix length must be 0 to {bits}");
            byte[] bytes = ip.GetAddressBytes();
            for (int i = 0; i < bytes.Length; i++)
            {
                int keep = Math.Clamp(prefix - i * 8, 0, 8);
                bytes[i] &= (byte)(0xFF << (8 - keep));
            }
            var network = new IPAddress(bytes);
            return (new NativeText("cidr", network + "/" + prefix, new Dictionary<string, object>
            {
                ["network"] = network,
                ["prefix"] = (double)prefix,
                ["bits"] = bits,
            }), null);
        }

        /// <summary>The block this text means, or an error that says why it isn't one.</summary>
        internal static NativeText Read(string text) => Make(new CidrType(), text);

        private static NativeText ReadIp(string text) => Make(new IpType(), text);

        private static NativeText Make(NativeType type, string text)
        {
            (object value, string reason) = type.Parse(text);
            return value is NativeText v ? v : throw new KScriptException($"'{text}' isn't {type.Info.WithArticle}: {reason}.", "Value");
        }

        /// <summary>True if <paramref name="ip"/> is in the block.</summary>
        internal static bool Contains(NativeText cidr, IPAddress ip)
        {
            IPAddress network = Network(cidr);
            if (ip.IsIPv4MappedToIPv6 && network.AddressFamily == AddressFamily.InterNetwork) ip = ip.MapToIPv4();
            if (ip.AddressFamily != network.AddressFamily) return false;
            byte[] a = ip.GetAddressBytes(), n = network.GetAddressBytes();
            int prefix = Prefix(cidr);
            for (int i = 0; i < a.Length; i++)
            {
                int keep = Math.Clamp(prefix - i * 8, 0, 8);
                byte mask = (byte)(0xFF << (8 - keep));
                if ((a[i] & mask) != n[i]) return false;
            }
            return true;
        }

        private static IPAddress Network(object cidr) => (IPAddress)((NativeText)cidr).Part("network");

        private static int Prefix(object cidr) => (int)(double)((NativeText)cidr).Part("prefix");

        private static int Bits(object cidr) => (int)((NativeText)cidr).Part("bits");

        private static IPAddress Last(object cidr)
        {
            IPAddress network = Network(cidr);
            BigInteger hostBits = (BigInteger.One << (Bits(cidr) - Prefix(cidr))) - 1;
            return IpType.FromNumber(IpType.Number(network) | hostBits, network.AddressFamily);
        }

        private static IEnumerable<object> Walk(object cidr)
        {
            int hostBits = Bits(cidr) - Prefix(cidr);
            if (hostBits > 16)
                throw new KScriptException($"{cidr} holds {Math.Pow(2, hostBits).ToString("0", CultureInfo.InvariantCulture)} addresses, and a for loop walks a block of at most {MaxWalk}. Use its .network and .last, or loop over a smaller block.", "Limit");
            IPAddress network = Network(cidr);
            BigInteger first = IpType.Number(network);
            return WalkFrom(first, 1 << hostBits, network.AddressFamily);
        }

        private static IEnumerable<object> WalkFrom(BigInteger first, int count, AddressFamily family)
        {
            for (int i = 0; i < count; i++) yield return IpType.Of(IpType.FromNumber(first + i, family));
        }
    }
}
