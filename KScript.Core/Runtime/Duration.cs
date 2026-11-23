namespace KScript.Runtime
{
    /// <summary>
    /// Lengths of time in arguments. Every argument that takes one (timeout, ttl, every, delay, wait...) reads it
    /// here, so they all accept the same things: a duration (<c>30s</c>, <c>1h30m</c>, <c>500ms</c>), text that
    /// reads as one (<c>'10m'</c>), or a plain number, which is always seconds.
    /// </summary>
    public static class Duration
    {
        /// <summary>The name of the native type.</summary>
        public const string TypeName = "duration";

        /// <summary>The seconds in a duration, in text such as '30s', or in a plain number. Throws for anything else.</summary>
        public static double Seconds(object value, string what)
        {
            if (TrySeconds(value, out double seconds)) return seconds;
            throw new KScriptException(
                $"{what} should be a length of time such as 30s, 5m or 1h30m (a plain number is seconds), not {(value is string s ? "'" + s + "'" : Values.Describe(value))}.", "Argument");
        }

        /// <summary>Like <see cref="Seconds"/>, but null stays null (no limit).</summary>
        public static double? SecondsOrNull(object value, string what) => value == null ? null : Seconds(value, what);

        /// <summary>True, with the seconds, when the value is a length of time.</summary>
        public static bool TrySeconds(object value, out double seconds)
        {
            seconds = 0;
            switch (Values.Unwrap(value))
            {
                case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                    seconds = d;
                    return true;
                case NativeText { TypeName: TypeName } native:
                    seconds = (double)native.Part("seconds");
                    return true;
                case string or ISemanticText:
                    if (!SemanticTypes.TryConvert(TypeName, Values.ToStr(value), out object converted, out _) || converted is not NativeText parsed) return false;
                    seconds = (double)parsed.Part("seconds");
                    return !double.IsNaN(seconds) && !double.IsInfinity(seconds);
                default:
                    return false;
            }
        }

        /// <summary>A duration value of this many seconds.</summary>
        public static object Of(double seconds) => DurationType.Of(seconds);
    }
}
