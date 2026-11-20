using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Konscious.Security.Cryptography;

namespace KScript.Runtime
{
    /// <summary>
    /// A text value with a meaning of its own, such as an email address or a handle. It prints as its text, string
    /// methods work on it, and it equals text that means the same thing once normalised.
    /// </summary>
    public interface ISemanticText : IKObject
    {
        /// <summary>The normalised text.</summary>
        string Text { get; }
    }

    /// <summary>
    /// The built-in types that check and normalise text: <c>email</c>, <c>password</c> and <c>handle</c>. Each
    /// converts from a string, or explains why it can't.
    /// </summary>
    public static class SemanticTypes
    {
        public const string Email = "email";
        public const string Password = "password";
        public const string Handle = "handle";
        public const string Uuid = "uuid";

        public static readonly string[] Names = { Email, Password, Handle, Uuid };

        public static bool IsSemantic(string type) => Names.Contains(type);

        /// <summary>
        /// Converts <paramref name="value"/> to the semantic type. Returns false with <paramref name="reason"/> set to
        /// why it doesn't fit (never quoting a password).
        /// </summary>
        public static bool TryConvert(string type, object value, out object result, out string reason)
        {
            result = null;
            switch (type)
            {
                case Email:
                    if (value is EmailAddress) { result = value; reason = null; return true; }
                    if (value is not string es) { reason = null; return false; }
                    if (!EmailAddress.TryParse(es, out EmailAddress email, out reason)) return false;
                    result = email;
                    return true;
                case Handle:
                    if (value is HandleName) { result = value; reason = null; return true; }
                    if (value is not string hs) { reason = null; return false; }
                    if (!HandleName.TryParse(hs, out HandleName handle, out reason)) return false;
                    result = handle;
                    return true;
                case Uuid:
                    if (value is UuidValue) { result = value; reason = null; return true; }
                    if (value is not string us) { reason = null; return false; }
                    if (!UuidValue.TryParse(us, out UuidValue uuid, out reason)) return false;
                    result = uuid;
                    return true;
                default:
                    if (value is PasswordValue) { result = value; reason = null; return true; }
                    if (value is not string ps) { reason = null; return false; }
                    if (!PasswordValue.TryCreate(ps, out PasswordValue password, out reason)) return false;
                    result = password;
                    return true;
            }
        }

        /// <summary>
        /// Why a value doesn't fit, starting "needs ...". The value itself is never shown for a password.
        /// </summary>
        public static string Mismatch(string type, object value, string reason)
        {
            if (type == Password)
                return value is string ? $"needs a password, but got text that {reason}" : $"needs a password (text), but got {"a " + Values.TypeOf(value)}";
            return reason != null ? $"needs {Article(type)} {type}, but got {Values.Describe(value)}: {reason}" : $"needs {Article(type)} {type}, but got {Values.Describe(value)}";
        }

        private static string Article(string type) => type == Email ? "an" : "a";

        /// <summary>The options a semantic type takes besides min, max, pattern, one_of and not.</summary>
        public static IReadOnlyList<string> OptionsOf(string type) => type switch
        {
            Uuid => new[] { "v" },
            Email => new[] { "domains" },
            Password => new[] { "upper", "lower", "digit", "symbol", "common", "score", "algorithm" },
            _ => Array.Empty<string>(),
        };
    }

    /// <summary>
    /// An email address. Spaces at either end are trimmed and the domain is lower-cased; the part before the @ keeps
    /// its case. <c>->local()</c> and <c>->domain()</c> give its parts.
    /// </summary>
    public sealed class EmailAddress : ISemanticText
    {
        private const string LocalSymbols = "!#$%&'*+-/=?^_`{|}~.";

        private EmailAddress(string local, string domain)
        {
            Local = local;
            Domain = domain;
        }

        public string Local { get; }
        public string Domain { get; }
        public string Text => Local + "@" + Domain;
        public string TypeName => SemanticTypes.Email;

        public static bool TryParse(string text, out EmailAddress email, out string reason)
        {
            email = null;
            string s = (text ?? string.Empty).Trim();
            reason = Problem(s);
            if (reason != null) return false;
            int at = s.IndexOf('@');
            email = new EmailAddress(s.Substring(0, at), s.Substring(at + 1).ToLowerInvariant());
            return true;
        }

        private static string Problem(string s)
        {
            if (s.Length == 0) return "it is empty";
            if (s.Length > 254) return "it is longer than 254 characters";
            int at = s.IndexOf('@');
            if (at < 0) return "it has no @";
            if (s.IndexOf('@', at + 1) >= 0) return "it has more than one @";
            string local = s.Substring(0, at);
            string domain = s.Substring(at + 1);
            if (local.Length == 0) return "there's nothing before the @";
            if (domain.Length == 0) return "there's nothing after the @";
            if (local.Length > 64) return "the part before the @ is longer than 64 characters";
            char bad = local.FirstOrDefault(c => !char.IsLetterOrDigit(c) && LocalSymbols.IndexOf(c) < 0);
            if (bad != default) return $"'{bad}' can't be used before the @";
            if (local.StartsWith('.') || local.EndsWith('.') || local.Contains("..")) return "the part before the @ can't start or end with a dot, or have two dots in a row";
            return DomainProblem(domain);
        }

        private static string DomainProblem(string domain)
        {
            string ascii;
            try
            {
                ascii = new IdnMapping().GetAscii(domain.ToLowerInvariant());
            }
            catch (ArgumentException)
            {
                return $"'{domain}' isn't a domain";
            }
            string[] labels = ascii.Split('.');
            if (labels.Length < 2) return "the domain needs a dot, as in example.com";
            foreach (string label in labels)
            {
                if (label.Length == 0) return "the domain has an empty part (two dots in a row, or a dot at either end)";
                if (label.Length > 63) return "a part of the domain is longer than 63 characters";
                if (label.StartsWith('-') || label.EndsWith('-')) return "a part of the domain starts or ends with -";
                if (!label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return $"'{domain}' isn't a domain";
            }
            string top = labels[^1];
            if (top.Length < 2 || !(top.All(char.IsAsciiLetter) || top.StartsWith("xn--", StringComparison.Ordinal))) return $"'{top}' isn't a top-level domain";
            return null;
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

        public override bool Equals(object obj) => obj is EmailAddress e && e.Text == Text;

        public override int GetHashCode() => Text.GetHashCode(StringComparison.Ordinal);

        public override string ToString() => Text;
    }

    /// <summary>
    /// A handle such as <c>@kyle_p</c>: an @ and then 1 to 30 letters, digits, _, . or -. It is lower-cased, so
    /// <c>'@Kyle'</c> and <c>'@kyle'</c> are the same handle. <c>->name()</c> is the name without the @.
    /// </summary>
    public sealed class HandleName : ISemanticText
    {
        public const int MaxLength = 30;

        private HandleName(string name) => Name = name;

        /// <summary>The name without the @.</summary>
        public string Name { get; }
        public string Text => "@" + Name;
        public string TypeName => SemanticTypes.Handle;

        public static bool TryParse(string text, out HandleName handle, out string reason)
        {
            handle = null;
            string s = (text ?? string.Empty).Trim();
            reason = Problem(s);
            if (reason != null) return false;
            handle = new HandleName(s.Substring(1).ToLowerInvariant());
            return true;
        }

        private static string Problem(string s)
        {
            if (s.Length == 0) return "it is empty";
            if (s[0] != '@') return $"it must start with @, as in @{(s.Length > 0 && s.Length <= MaxLength ? s.ToLowerInvariant() : "name")}";
            string name = s.Substring(1);
            if (name.Length == 0) return "it needs a name after the @";
            if (name.Length > MaxLength) return $"the name is longer than {MaxLength} characters";
            char bad = name.FirstOrDefault(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'));
            if (bad != default) return $"'{bad}' can't be used in a handle (use letters, digits, _, . and -)";
            if (name[0] is '.' or '-') return "the name must start with a letter, a digit or _";
            if (name.EndsWith('.') || name.EndsWith('-')) return "the name can't end with . or -";
            if (name.Contains("..")) return "the name can't have two dots in a row";
            return null;
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

        public override bool Equals(object obj) => obj is HandleName h && h.Name == Name;

        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);

        public override string ToString() => Text;
    }

    /// <summary>
    /// A password. It prints as <c>********</c> everywhere (echo, logs, JSON, errors), can't be compared with
    /// <c>==</c>, and is checked with <c>->verify($hash)</c>. <c>->reveal()</c> is the only way to get its text.
    /// </summary>
    public sealed class PasswordValue : IKObject
    {
        public const string Mask = "********";
        public const int MaxLength = 1024;

        // Argon2id settings: OWASP's recommended minimum (19 MiB, 2 passes, 1 lane).
        internal const int ArgonMemory = 19456;
        internal const int ArgonPasses = 2;
        internal const int ArgonLanes = 1;
        internal const int BcryptCost = 12;
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        private readonly string secret;

        private PasswordValue(string secret, string algorithm = null)
        {
            this.secret = secret;
            Algorithm = algorithm;
        }

        /// <summary>The algorithm ->hash() uses when none is given: the type's algorithm option, or argon2id.</summary>
        public string Algorithm { get; }

        internal PasswordValue WithAlgorithm(string algorithm) => new PasswordValue(secret, algorithm);

        public string TypeName => SemanticTypes.Password;

        /// <summary>The password's text. Only <c>->reveal()</c> and the hashing code read it.</summary>
        internal string Secret => secret;

        public static bool TryCreate(string text, out PasswordValue password, out string reason)
        {
            password = null;
            reason = text.Length == 0 ? "is empty" : text.Length > MaxLength ? $"is longer than {MaxLength} characters" : null;
            if (reason != null) return false;
            password = new PasswordValue(text);
            return true;
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

        public string ToText() => Mask;

        public override string ToString() => Mask;

        /// <summary>Thrown by <c>==</c>, <c>&lt;</c> and the rest when one side is a password.</summary>
        public static KScriptException CompareError() => new KScriptException(
            "Passwords can't be compared with operators. Use $password->verify($hash) to check one against a stored hash.", "Type");

        #region Hashing

        /// <summary>A hash to store: argon2id (the default) or bcrypt, with a new random salt each time.</summary>
        public string Hash(string algorithm)
        {
            switch ((algorithm ?? "argon2id").Trim().ToLowerInvariant())
            {
                case "argon2id":
                {
                    byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
                    byte[] hash = Argon2(secret, salt, ArgonMemory, ArgonPasses, ArgonLanes, HashBytes);
                    return $"$argon2id$v=19$m={ArgonMemory},t={ArgonPasses},p={ArgonLanes}${B64(salt)}${B64(hash)}";
                }
                case "bcrypt":
                    if (Encoding.UTF8.GetByteCount(secret) > 72)
                        throw new KScriptException("bcrypt only uses the first 72 bytes of a password, so this one can't be hashed with it. Use the default, argon2id.", "Value");
                    return BCrypt.Net.BCrypt.HashPassword(secret, BcryptCost);
                default:
                    throw new KScriptException($"'{algorithm}' isn't a password hashing algorithm. Use argon2id (the default) or bcrypt.", "Value");
            }
        }

        /// <summary>True if this password is the one <paramref name="stored"/> was made from. The comparison takes the same time either way.</summary>
        public bool Verify(string stored)
        {
            string hash = (stored ?? string.Empty).Trim();
            if (TryParseArgon(hash, out int memory, out int passes, out int lanes, out byte[] salt, out byte[] expected))
                return CryptographicOperations.FixedTimeEquals(Argon2(secret, salt, memory, passes, lanes, expected.Length), expected);
            if (IsBcrypt(hash))
            {
                try
                {
                    return BCrypt.Net.BCrypt.Verify(secret, hash);
                }
                catch (BCrypt.Net.SaltParseException)
                {
                    // Falls through to the error below.
                }
            }
            throw new KScriptException("->verify() needs a hash made by ->hash() (it starts with $argon2id$ or $2b$), but got something else.", "Value");
        }

        /// <summary>True if <paramref name="stored"/> was made with weaker settings than ->hash() uses now, so it should be made again at the next login.</summary>
        public static bool NeedsRehash(string stored)
        {
            string hash = (stored ?? string.Empty).Trim();
            if (TryParseArgon(hash, out int memory, out int passes, out int lanes, out _, out byte[] expected))
                return memory < ArgonMemory || passes < ArgonPasses || lanes < ArgonLanes || expected.Length < HashBytes;
            if (IsBcrypt(hash) && int.TryParse(hash.AsSpan(4, 2), out int cost)) return cost < BcryptCost;
            throw new KScriptException("->needs_rehash() needs a hash made by ->hash() (it starts with $argon2id$ or $2b$), but got something else.", "Value");
        }

        private static bool IsBcrypt(string hash) => hash.Length == 60 && (hash.StartsWith("$2a$", StringComparison.Ordinal) || hash.StartsWith("$2b$", StringComparison.Ordinal) || hash.StartsWith("$2y$", StringComparison.Ordinal));

        private static byte[] Argon2(string password, byte[] salt, int memory, int passes, int lanes, int length)
        {
            using var argon = new Argon2id(Encoding.UTF8.GetBytes(password)) { Salt = salt, MemorySize = memory, Iterations = passes, DegreeOfParallelism = lanes };
            return argon.GetBytes(length);
        }

        /// <summary>Reads <c>$argon2id$v=19$m=19456,t=2,p=1$salt$hash</c> (the PHC string format).</summary>
        private static bool TryParseArgon(string text, out int memory, out int passes, out int lanes, out byte[] salt, out byte[] hash)
        {
            memory = passes = lanes = 0;
            salt = hash = null;
            string[] parts = text.Split('$');
            if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != "argon2id" || parts[2] != "v=19") return false;
            foreach (string setting in parts[3].Split(','))
            {
                string[] kv = setting.Split('=');
                if (kv.Length != 2 || !int.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1) return false;
                switch (kv[0])
                {
                    case "m": memory = n; break;
                    case "t": passes = n; break;
                    case "p": lanes = n; break;
                    default: return false;
                }
            }
            // Limits stop a stored hash from asking for gigabytes of memory or hours of work.
            if (memory < 8 * lanes || memory > 1 << 20 || passes > 100 || lanes > 64) return false;
            salt = FromB64(parts[4]);
            hash = FromB64(parts[5]);
            return salt != null && hash != null && salt.Length >= 8 && hash.Length >= 16;
        }

        private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

        private static byte[] FromB64(string text)
        {
            string padded = text + new string('=', (4 - text.Length % 4) % 4);
            try
            {
                return Convert.FromBase64String(padded);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        #endregion

        #region Strength

        private static readonly HashSet<string> Common = new HashSet<string>(StringComparer.Ordinal)
        {
            "123456", "123456789", "12345678", "password", "qwerty", "123123", "12345", "1234567", "111111", "1234567890",
            "000000", "abc123", "password1", "iloveyou", "1q2w3e4r", "qwerty123", "123321", "666666", "654321", "7777777",
            "123", "dragon", "1qaz2wsx", "monkey", "letmein", "football", "baseball", "welcome", "admin", "login",
            "princess", "sunshine", "master", "shadow", "superman", "qwertyuiop", "michael", "passw0rd", "trustno1", "starwars",
            "whatever", "freedom", "hello", "charlie", "aa123456", "donald", "password123", "qwerty1", "zaq1zaq1", "access",
            "flower", "hottie", "loveme", "121212", "mustang", "batman", "ninja", "azerty", "solo", "jesus",
            "computer", "secret", "summer", "winter", "spring", "autumn", "changeme", "default", "test", "test123",
            "guest", "root", "toor", "pass", "pass123", "p@ssw0rd", "p@ssword", "welcome1", "letmein1", "abcdef",
            "abcd1234", "1234abcd", "qazwsx", "asdfgh", "asdfghjkl", "zxcvbnm", "11111111", "88888888", "987654321", "q1w2e3r4",
            "google", "hunter2", "cheese", "pokemon", "chocolate", "liverpool", "arsenal", "chelsea", "jordan23", "killer",
        };

        private static readonly string[] Labels = { "very weak", "weak", "fair", "good", "strong" };

        /// <summary>
        /// How hard the password is to guess (score 0 to 4) and which of the rules it breaks. Rules: min (8),
        /// max, upper, lower, digit, symbol, common (yes: not a well-known password), not (text it mustn't
        /// contain, such as the user's name) and score (3: the lowest score that passes).
        /// </summary>
        public Dictionary<string, object> Strength(CallContext c)
        {
            List<string> problems = RuleProblems(c.Int(0, 8), c.Arg(1) == null ? null : c.Int(1), c.Bool(2), c.Bool(3), c.Bool(4), c.Bool(5), c.Bool(6, true), Words(c.Arg(7)), Math.Clamp(c.Int(8, 3), 0, 4));
            int score = IsCommon(secret) ? 0 : Score(secret);
            return new Dictionary<string, object>
            {
                ["score"] = (double)score,
                ["label"] = Labels[score],
                ["ok"] = problems.Count == 0,
                ["problems"] = problems.Cast<object>().ToList(),
            };
        }

        /// <summary>The rules this password breaks, as messages such as "needs a digit".</summary>
        internal List<string> RuleProblems(int min, int? max, bool upper, bool lower, bool digit, bool symbol, bool common, IEnumerable<string> not, int score)
        {
            var problems = new List<string>();
            if (secret.Length < min) problems.Add($"must be at least {min} characters");
            if (max is int most && secret.Length > most) problems.Add($"must be at most {most} characters");
            if (upper && !secret.Any(char.IsUpper)) problems.Add("needs an upper-case letter");
            if (lower && !secret.Any(char.IsLower)) problems.Add("needs a lower-case letter");
            if (digit && !secret.Any(char.IsDigit)) problems.Add("needs a digit");
            if (symbol && secret.All(char.IsLetterOrDigit)) problems.Add("needs a symbol");
            bool isCommon = IsCommon(secret);
            if (common && isCommon) problems.Add("is one of the most common passwords");
            foreach (string word in not ?? Enumerable.Empty<string>())
            {
                if (secret.Contains(word, StringComparison.OrdinalIgnoreCase)) problems.Add($"mustn't contain '{word}'");
            }
            if ((isCommon ? 0 : Score(secret)) < score && !(common && isCommon)) problems.Add("is too easy to guess");
            return problems;
        }

        private static IEnumerable<string> Words(object value) => value switch
        {
            null => Enumerable.Empty<string>(),
            List<object> list => list.Select(Values.ToStr).Select(s => s.Trim()).Where(s => s.Length >= 3),
            _ => new[] { Values.ToStr(value).Trim() }.Where(s => s.Length >= 3),
        };

        private static bool IsCommon(string password)
        {
            string lower = password.ToLowerInvariant();
            return Common.Contains(lower) || Common.Contains(lower.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '!'));
        }

        /// <summary>
        /// An estimate of the guesses needed, in bits: the length times the size of the alphabet used, where
        /// characters that repeat or run in sequence (aaa, abc, 321) count for little. 0 is under 28 bits, 4 is 80+.
        /// </summary>
        internal static int Score(string password)
        {
            int pool = 0;
            if (password.Any(char.IsLower)) pool += 26;
            if (password.Any(char.IsUpper)) pool += 26;
            if (password.Any(char.IsDigit)) pool += 10;
            if (password.Any(c => c < 128 && !char.IsLetterOrDigit(c))) pool += 33;
            if (password.Any(c => c >= 128 && !char.IsLetterOrDigit(c) || c >= 128 && char.IsLetter(c) && !char.IsUpper(c) && !char.IsLower(c))) pool += 100;
            double effective = 0;
            for (int i = 0; i < password.Length; i++)
            {
                bool predictable = i > 0 && (password[i] == password[i - 1] || Math.Abs(password[i] - password[i - 1]) == 1);
                effective += predictable ? 0.25 : 1;
            }
            double bits = effective * Math.Log2(Math.Max(pool, 1));
            return bits < 28 ? 0 : bits < 36 ? 1 : bits < 60 ? 2 : bits < 80 ? 3 : 4;
        }

        #endregion
    }

    /// <summary>
    /// A UUID such as <c>3f2a8c1e-9b7d-4c2a-8e1f-0a9b8c7d6e5f</c>, in lower case with dashes. <c>->version()</c> is
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
