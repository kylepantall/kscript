using System.Globalization;

using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Data
{
    /// <summary>
    /// A stored password: the hash a <c>password</c> field keeps, never the password itself. <c>->verify(text)</c>
    /// checks a password someone typed against it, and <c>->needs_rehash()</c> says when to hash it again.
    /// </summary>
    internal sealed class PasswordHash : IKObject
    {
        public PasswordHash(string stored) => Stored = stored;

        public string Stored { get; }

        public string TypeName => KType.PasswordHash;

        public string ToText() => PasswordValue.Mask;

        public override string ToString() => PasswordValue.Mask;

        public override bool Equals(object obj) => obj is PasswordHash h && h.Stored == Stored;

        public override int GetHashCode() => Stored.GetHashCode(StringComparison.Ordinal);

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

        /// <summary>True when <paramref name="password"/> (a password, or text) is the one this hash was made from.</summary>
        public bool Verify(object password)
        {
            PasswordValue value = password as PasswordValue;
            if (value == null)
            {
                string text = Values.ToStr(password);
                if (!PasswordValue.TryCreate(text, out value, out _)) return false;
            }
            try
            {
                return value.Verify(Stored);
            }
            catch (KScriptException)
            {
                // A stored value that isn't a hash (written by something else) matches no password.
                return false;
            }
        }
    }

    /// <summary>Turns KScript values into what the database stores, and stored values back into KScript values.</summary>
    internal static class Rows
    {
        internal const string DateFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

        /// <summary>
        /// What the clock says now, to the microsecond: Postgres and MySQL keep no finer times, so a record reads back
        /// the times it was saved with. Tests replace it.
        /// </summary>
        internal static Func<DateTime> Clock = Now;

        internal static DateTime Now()
        {
            DateTime now = DateTime.Now;
            return now.AddTicks(-(now.Ticks % 10));
        }

        /// <summary>A value as a parameter, for a column (or, with no column, as the value itself suggests).</summary>
        public static object ToDb(object value, Column column, Dialect dialect)
        {
            switch (value)
            {
                case null:
                    return null;
                case BareWord word:
                    return word.Name;
                case bool b:
                    return dialect.Bool(b);
                case double d when column?.Kind == ColumnKind.Integer || (column == null && d == Math.Floor(d) && Math.Abs(d) < 9e15):
                    return (long)d;
                case double d:
                    return d;
                case string s when column?.Kind == ColumnKind.Integer && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n):
                    return n;
                case DateTime dt:
                    return dialect.Date(dt);
                case PasswordValue password:
                    return password.Hash(password.Algorithm);
                case PasswordHash hash:
                    return hash.Stored;
                case List<object> or Dictionary<string, object>:
                    return Values.ToJson(value);
                case string s:
                    return s;
                case IKObject o:
                    return o.ToText();
                default:
                    return Values.ToStr(value);
            }
        }

        /// <summary>
        /// A value for a column: checked and converted to the column's type first (an email is normalised, an enum
        /// word checked, a date read from text), then as the database stores it.
        /// </summary>
        public static object ForColumn(object value, Column column, Dialect dialect)
        {
            if (value is BareWord word) value = word.Name;
            if (column == null || value == null) return ToDb(value, column, dialect);
            if (column.Kind == ColumnKind.Password && value is not PasswordHash && value is not PasswordValue)
                throw new KScriptException($"{column.Name} holds password hashes, so it can't be compared with or set to text. Find the record, then check with ->verify().", "Type");
            object converted = column.Kind switch
            {
                ColumnKind.Integer or ColumnKind.Real => Values.ConvertTo(value, "number", column.Name),
                ColumnKind.Bool => Values.ConvertTo(value, "bool", column.Name),
                ColumnKind.Date => Values.ConvertTo(value, "date", column.Name),
                ColumnKind.Email => Values.ConvertTo(value, SemanticTypes.Email, column.Name),
                ColumnKind.Handle => Values.ConvertTo(value, SemanticTypes.Handle, column.Name),
                ColumnKind.Uuid => Values.ConvertTo(value, SemanticTypes.Uuid, column.Name),
                ColumnKind.Enum => Word(value, column),
                _ => value,
            };
            return ToDb(converted, column, dialect);
        }

        private static object Word(object value, Column column)
        {
            string text = Values.ToStr(value);
            string found = column.Words.FirstOrDefault(w => string.Equals(w, text, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
            string close = Values.Closest(text, column.Words);
            throw new KScriptException($"'{text}' isn't a {column.Field.Type.Name} for {column.Name}." + (close != null ? $" Did you mean {close}?" : string.Empty) + $" Use one of: {string.Join(", ", column.Words)}.", "Value");
        }

        /// <summary>A stored value as the column's KScript value: a number, bool, date, email, enum word, list or map.</summary>
        public static object FromDb(object raw, Column column)
        {
            if (raw == null || raw is DBNull) return null;
            if (column == null) return Plain(raw);
            switch (column.Kind)
            {
                case ColumnKind.Integer:
                case ColumnKind.Real:
                    return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                case ColumnKind.Bool:
                    return raw is bool b ? b : Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0;
                case ColumnKind.Date:
                    return raw is DateTime dt ? dt : DateTime.Parse(Values.ToStr(Plain(raw)), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);
                case ColumnKind.Email:
                case ColumnKind.Handle:
                case ColumnKind.Uuid:
                {
                    string text = Values.ToStr(Plain(raw));
                    string kind = column.Kind == ColumnKind.Email ? SemanticTypes.Email : column.Kind == ColumnKind.Handle ? SemanticTypes.Handle : SemanticTypes.Uuid;
                    // A value the database holds that isn't valid (written by something else) is still read, as text.
                    return SemanticTypes.TryConvert(kind, text, out object value, out _) ? value : text;
                }
                case ColumnKind.Password:
                    return new PasswordHash(Values.ToStr(Plain(raw)));
                case ColumnKind.Json:
                {
                    string text = Values.ToStr(Plain(raw));
                    try
                    {
                        return Values.ParseJson(text);
                    }
                    catch (KScriptException)
                    {
                        return text;
                    }
                }
                default:
                    return Values.ToStr(Plain(raw));
            }
        }

        /// <summary>A value from a table with no model: numbers, text and nulls.</summary>
        public static object Plain(object raw) => raw switch
        {
            null => null,
            DBNull => null,
            long l => (double)l,
            ulong l => (double)l,
            int i => (double)i,
            uint i => (double)i,
            short s => (double)s,
            ushort s => (double)s,
            byte b => (double)b,
            sbyte b => (double)b,
            decimal m => (double)m,
            float f => (double)f,
            double d => d,
            bool b => b,
            string s => s,
            DateTime dt => dt,
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => Convert.ToString(raw, CultureInfo.InvariantCulture),
        };

        /// <summary>A row of a table with no model, as a map.</summary>
        public static Dictionary<string, object> Map(Dictionary<string, object> row) =>
            row.ToDictionary(kv => kv.Key, kv => Plain(kv.Value), StringComparer.Ordinal);
    }
}
