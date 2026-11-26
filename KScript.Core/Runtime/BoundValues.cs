using KScript.Syntax.Markup;

namespace KScript.Runtime
{
    /// <summary>
    /// A bound value on its way into its field, as the binding's trim: and as: say. Pages (@bind) and windows
    /// (TextBox(bind: $name, trim: true)) both read values through this one place.
    /// </summary>
    public static class BoundValues
    {
        /// <summary>The value after trim: and as:, or what is wrong with it when as: refuses it.</summary>
        public static (object Value, string Problem) Read(object value, BindDirective.Reading binding, string field)
        {
            if (binding == null) return (value, null);
            if (binding.Trim && value is string untrimmed) value = untrimmed.Trim();
            return binding.Word("as") is string type ? As(value, type, field) : (value, null);
        }

        /// <summary>A value read as a type (string, int, number, bool), with what is wrong when it can't be.</summary>
        public static (object Value, string Problem) As(object value, string type, string field)
        {
            string text = Values.ToStr(value)?.Trim() ?? string.Empty;
            switch (type)
            {
                case "string":
                    return (Values.ToStr(value), null);
                case "bool":
                    return value is bool b ? (b, null) : Values.ParseBool(text) is bool parsed ? (parsed, null) : (null, $"{field} takes true or false, and '{text}' is neither.");
                case "int":
                    return Values.TryNumber(text, out double whole) && whole == Math.Floor(whole) ? (whole, null) : (null, $"{field} takes a whole number, and '{text}' isn't one.");
                default:
                    return Values.TryNumber(text, out double number) ? (number, null) : (null, $"{field} takes a number, and '{text}' isn't one.");
            }
        }
    }
}
