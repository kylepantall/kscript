using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Konscious.Security.Cryptography;

namespace KScript.Runtime
{
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
                catch (Exception ex) when (ex is BCrypt.Net.SaltParseException || ex is FormatException)
                {
                    // Falls through to the error below.
                }
            }
            throw new KScriptException("->verify() needs a hash made by ->hash() (it starts with $argon2id$ or $2b$), but got something else.", "Value");
        }

        /// <summary>
        /// True if <paramref name="stored"/> was made with weaker settings than ->hash() uses now, or with another
        /// algorithm than <paramref name="algorithm"/>, so it should be made again at the next login.
        /// </summary>
        public static bool NeedsRehash(string stored, string algorithm = "argon2id")
        {
            string hash = (stored ?? string.Empty).Trim();
            if (TryParseArgon(hash, out int memory, out int passes, out int lanes, out _, out byte[] expected))
                return algorithm == "bcrypt" || memory < ArgonMemory || passes < ArgonPasses || lanes < ArgonLanes || expected.Length < HashBytes;
            if (IsBcrypt(hash) && int.TryParse(hash.AsSpan(4, 2), out int cost)) return algorithm != "bcrypt" || cost < BcryptCost;
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
            int distinct = password.Distinct().Count();
            for (int i = 0; i < password.Length; i++)
            {
                bool predictable = i > 0 && (password[i] == password[i - 1] || Math.Abs(password[i] - password[i - 1]) == 1);
                effective += predictable ? 0.25 : 1;
            }
            effective = Math.Min(effective, distinct * 2);
            double bits = effective * Math.Log2(Math.Max(pool, 1));
            return bits < 28 ? 0 : bits < 36 ? 1 : bits < 60 ? 2 : bits < 80 ? 3 : 4;
        }

        #endregion
    }
}
