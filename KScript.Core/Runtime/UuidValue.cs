using System.Security.Cryptography;
using System.Text;

namespace KScript.Runtime
{
    /// <summary>
    /// A UUID such as <c>3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f</c>, in lower case with dashes. <c>.version</c> is
    /// its version (4 for random ones, 8 for custom ones). <c>@uuid()</c> makes new ones.
    /// </summary>
    public sealed class UuidValue : ISemanticText
    {
        private UuidValue(string text) => Text = text;

        public string Text { get; }
        public string TypeName => SemanticTypes.Uuid;

        /// <summary>The version: the first digit of the third group (0 for the nil UUID, 15 for the max UUID).</summary>
        public int Version => Convert.ToInt32(Text.Substring(14, 1), 16);

        public static bool TryParse(string text, out UuidValue uuid, out string reason)
        {
            uuid = null;
            string s = (text ?? string.Empty).Trim().ToLowerInvariant();
            if (s.StartsWith("urn:uuid:", StringComparison.Ordinal)) s = s.Substring(9);
            if (s.Length == 38 && s[0] == '{' && s[^1] == '}') s = s.Substring(1, 36);
            if (s.Length == 32 && s.All(char.IsAsciiHexDigit)) s = $"{s.Substring(0, 8)}-{s.Substring(8, 4)}-{s.Substring(12, 4)}-{s.Substring(16, 4)}-{s.Substring(20)}";
            reason = Problem(s);
            if (reason != null) return false;
            uuid = new UuidValue(s);
            return true;
        }

        private static string Problem(string s)
        {
            if (s.Length == 0) return "it is empty";
            if (s.Length != 36 || s[8] != '-' || s[13] != '-' || s[18] != '-' || s[23] != '-')
                return "a UUID is 32 hex digits in groups of 8-4-4-4-12, such as 3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f";
            if (!s.Where(c => c != '-').All(char.IsAsciiHexDigit)) return "a UUID only has the hex digits 0-9 and a-f";
            if (s == "00000000-0000-0000-0000-000000000000" || s == "ffffffff-ffff-ffff-ffff-ffffffffffff") return null;
            if (s[14] is '0' or '9' or 'a' or 'b' or 'c' or 'd' or 'e' or 'f') return $"its version digit is {s[14]}, but UUIDs have versions 1 to 8";
            if (s[19] is not ('8' or '9' or 'a' or 'b')) return "its variant digit (the first of the fourth group) must be 8, 9, a or b";
            return null;
        }

        /// <summary>
        /// A new UUID. Version 4 is random. Version 8 holds what you choose: with a name, it is a SHA-256 hash of the
        /// namespace and the name (the same name gives the same UUID); without one, the time in milliseconds then
        /// random bits, so new ones sort after old ones.
        /// </summary>
        public static UuidValue New(int version, string name = null, string space = null)
        {
            byte[] bytes;
            switch (version)
            {
                case 4:
                    bytes = RandomNumberGenerator.GetBytes(16);
                    break;
                case 8 when name != null:
                    bytes = SHA256.HashData(Encoding.UTF8.GetBytes((space ?? string.Empty) + "\0" + name)).AsSpan(0, 16).ToArray();
                    break;
                case 8:
                    bytes = RandomNumberGenerator.GetBytes(16);
                    long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    for (int i = 0; i < 6; i++) bytes[i] = (byte)(ms >> (8 * (5 - i)));
                    break;
                default:
                    throw new KScriptException($"@uuid() makes version 4 (random) or 8 (custom) UUIDs, not version {version}.", "Value");
            }
            bytes[6] = (byte)((bytes[6] & 0x0F) | (version << 4));
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
            string hex = Convert.ToHexString(bytes).ToLowerInvariant();
            return new UuidValue($"{hex.Substring(0, 8)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}-{hex.Substring(16, 4)}-{hex.Substring(20)}");
        }

        public bool TryGetMember(string name, out object value)
        {
            value = null;
            return false;
        }

        public bool TryCallMethod(CallContext call, string name, out object result)
        {
            result = null;
            return false;
        }

        public string ToText() => Text;

        public override bool Equals(object obj) => obj is UuidValue u && u.Text == Text;

        public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);

        public override string ToString() => Text;
    }
}
