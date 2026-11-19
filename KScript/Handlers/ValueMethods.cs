using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using KScript.KScriptExceptions;

namespace KScript.Handlers
{
    /// <summary>
    /// The value a method is called on, with its arguments.
    /// </summary>
    public sealed class MethodCallContext
    {
        internal MethodCallContext(string method, string value, string[] args, string variableName, KScriptContainer container)
        {
            Method = method;
            Value = value ?? string.Empty;
            Args = args;
            VariableName = variableName;
            Container = container;
        }

        public string Method { get; }

        /// <summary>The value the method was called on.</summary>
        public string Value { get; }

        public string[] Args { get; }

        /// <summary>The variable the value came from (for <c>$x->increment()</c>), or null for literals and results.</summary>
        public string VariableName { get; }

        public KScriptContainer Container { get; }

        public double Number => ValueTypes.ToNumber(Value, Method);

        public double NumberArg(int index) => ValueTypes.ToNumber(Args[index], Method);

        public bool Bool => ValueTypes.ToBool(Value, Method);

        public int IntArg(int index) => (int)Math.Round(NumberArg(index));
    }

    /// <summary>
    /// Type inference for KScript's string values: a value is a number if it parses as one (with '.' as the
    /// decimal separator), a bool if it is one of yes/no/true/false/y/n/t/f/1/0, and a string otherwise.
    /// </summary>
    public static class ValueTypes
    {
        public static bool IsNumber(string value) =>
            double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        public static bool IsBool(string value) => !string.IsNullOrEmpty(value) && KScriptBoolHandler.IsBool(value.Trim());

        public static string TypeOf(string value)
        {
            if (value == null || value == Global.Values.NULL) return "null";
            if (IsNumber(value)) return "number";
            if (IsBool(value)) return "bool";
            return "string";
        }

        public static double ToNumber(string value, string method)
        {
            if (!double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
            {
                throw new KScriptException("Type_Error", $"'{method}()' expects a number but got '{value}'.");
            }
            return result;
        }

        public static bool ToBool(string value, string method)
        {
            if (!IsBool(value))
            {
                throw new KScriptException("Type_Error", $"'{method}()' expects a bool (yes/no) but got '{value}'.");
            }
            return KScriptBoolHandler.Convert(value.Trim());
        }

        public static string FromNumber(double value) => value.ToString(CultureInfo.InvariantCulture);

        public static string FromBool(bool value) => value ? Global.Booleans.YES : Global.Booleans.NO;
    }

    /// <summary>
    /// Built-in methods that can be called on any value with <c>value->method(args)</c> or <c>value |> method(args)</c>.
    /// </summary>
    public static class ValueMethods
    {
        private sealed class Method
        {
            public Method(int min, int max, Func<MethodCallContext, string> body, string description)
            {
                MinArgs = min;
                MaxArgs = max;
                Body = body;
                Description = description;
            }

            public int MinArgs { get; }
            public int MaxArgs { get; }
            public Func<MethodCallContext, string> Body { get; }
            public string Description { get; }
        }

        private static readonly Dictionary<string, Method> Methods = new Dictionary<string, Method>(StringComparer.OrdinalIgnoreCase);

        private static void Add(string name, int min, int max, string description, Func<MethodCallContext, string> body) =>
            Methods[name] = new Method(min, max, body, description);

        private static string Num(double d) => ValueTypes.FromNumber(d);
        private static string Bool(bool b) => ValueTypes.FromBool(b);

        static ValueMethods()
        {
            // ---- Any value ----
            Add("type", 0, 0, "number, bool, string or null", c => ValueTypes.TypeOf(c.Value));
            Add("length", 0, 0, "number of characters", c => Num(c.Value.Length));
            Add("is_number", 0, 0, "yes if the value is a number", c => Bool(ValueTypes.IsNumber(c.Value)));
            Add("is_bool", 0, 0, "yes if the value is yes/no/true/false", c => Bool(ValueTypes.IsBool(c.Value)));
            Add("is_empty", 0, 0, "yes if empty, whitespace or NULL", c => Bool(string.IsNullOrWhiteSpace(c.Value) || c.Value == Global.Values.NULL));
            Add("has_value", 0, 0, "yes if not empty", c => Bool(!string.IsNullOrEmpty(c.Value)));
            Add("is_url", 0, 0, "yes if the value is an absolute URL",
                c => Bool(Uri.TryCreate(c.Value, UriKind.Absolute, out Uri u) && u.Scheme != Uri.UriSchemeFile));
            Add("equals", 1, 2, "equals(other, ignore_case?)", c => Bool(c.Args.Length > 1 && ValueTypes.ToBool(c.Args[1], c.Method)
                ? string.Equals(c.Value, c.Args[0], StringComparison.OrdinalIgnoreCase)
                : c.Value == c.Args[0]));
            Add("default", 1, 1, "default(fallback): the fallback if the value is empty or NULL",
                c => string.IsNullOrWhiteSpace(c.Value) || c.Value == Global.Values.NULL ? c.Args[0] : c.Value);
            Add("call", 0, int.MaxValue, "call(args...): call a lambda stored in the value", null);

            // ---- Strings ----
            Add("upper", 0, 0, "upper case", c => c.Value.ToUpperInvariant());
            Add("lower", 0, 0, "lower case", c => c.Value.ToLowerInvariant());
            Add("trim", 0, 0, "remove surrounding whitespace", c => c.Value.Trim());
            Add("trim_start", 0, 0, "remove leading whitespace", c => c.Value.TrimStart());
            Add("trim_end", 0, 0, "remove trailing whitespace", c => c.Value.TrimEnd());
            Add("reverse", 0, 0, "reverse the characters", c => new string(c.Value.Reverse().ToArray()));
            Add("contains", 1, 1, "contains(text)", c => Bool(c.Value.Contains(c.Args[0])));
            Add("starts_with", 1, 1, "starts_with(text)", c => Bool(c.Value.StartsWith(c.Args[0], StringComparison.Ordinal)));
            Add("ends_with", 1, 1, "ends_with(text)", c => Bool(c.Value.EndsWith(c.Args[0], StringComparison.Ordinal)));
            Add("index_of", 1, 1, "index_of(text): position or -1", c => Num(c.Value.IndexOf(c.Args[0], StringComparison.Ordinal)));
            Add("replace", 2, 2, "replace(old, new)", c => c.Args[0].Length == 0 ? c.Value : c.Value.Replace(c.Args[0], c.Args[1]));
            Add("substring", 1, 2, "substring(start, length?)", c =>
            {
                int start = Clamp(c.IntArg(0), 0, c.Value.Length);
                int length = c.Args.Length > 1 ? Clamp(c.IntArg(1), 0, c.Value.Length - start) : c.Value.Length - start;
                return c.Value.Substring(start, length);
            });
            Add("char_at", 1, 1, "char_at(index)", c =>
            {
                int index = c.IntArg(0);
                return index >= 0 && index < c.Value.Length ? c.Value[index].ToString() : string.Empty;
            });
            Add("split", 2, 2, "split(separator, index): the part at index", c =>
            {
                string[] parts = c.Value.Split(new[] { c.Args[0] }, StringSplitOptions.None);
                int index = c.IntArg(1);
                if (index < 0 || index >= parts.Length)
                {
                    throw new KScriptException("Index_Out_Of_Range", $"split(): index {index} is out of range (there are {parts.Length} parts).");
                }
                return parts[index];
            });
            Add("count", 1, 1, "count(text): occurrences of text", c => Num(c.Args[0].Length == 0 ? 0
                : (c.Value.Length - c.Value.Replace(c.Args[0], string.Empty).Length) / c.Args[0].Length));
            Add("repeat", 1, 1, "repeat(times)", c => string.Concat(Enumerable.Repeat(c.Value, Math.Max(0, c.IntArg(0)))));
            Add("pad_left", 1, 2, "pad_left(width, char?)", c => c.Value.PadLeft(c.IntArg(0), PadChar(c)));
            Add("pad_right", 1, 2, "pad_right(width, char?)", c => c.Value.PadRight(c.IntArg(0), PadChar(c)));

            // ---- Numbers ----
            Add("add", 1, 1, "add(n)", c => Num(c.Number + c.NumberArg(0)));
            Add("sub", 1, 1, "sub(n)", c => Num(c.Number - c.NumberArg(0)));
            Add("mul", 1, 1, "mul(n)", c => Num(c.Number * c.NumberArg(0)));
            Add("div", 1, 1, "div(n)", c =>
            {
                double divisor = c.NumberArg(0);
                if (divisor == 0)
                {
                    throw new KScriptException("Math_Error", $"div(): cannot divide '{c.Value}' by zero.");
                }
                return Num(c.Number / divisor);
            });
            Add("mod", 1, 1, "mod(n): remainder", c => Num(c.Number % c.NumberArg(0)));
            Add("pow", 1, 1, "pow(n)", c => Num(Math.Pow(c.Number, c.NumberArg(0))));
            Add("round", 0, 1, "round(decimals?)", c => Num(Math.Round(c.Number, c.Args.Length > 0 ? Clamp(c.IntArg(0), 0, 15) : 0, MidpointRounding.AwayFromZero)));
            Add("floor", 0, 0, "round down", c => Num(Math.Floor(c.Number)));
            Add("ceil", 0, 0, "round up", c => Num(Math.Ceiling(c.Number)));
            Add("abs", 0, 0, "absolute value", c => Num(Math.Abs(c.Number)));
            Add("min", 1, 1, "min(n): the smaller value", c => Num(Math.Min(c.Number, c.NumberArg(0))));
            Add("max", 1, 1, "max(n): the larger value", c => Num(Math.Max(c.Number, c.NumberArg(0))));
            Add("lt", 1, 1, "lt(n): less than", c => Bool(c.Number < c.NumberArg(0)));
            Add("lte", 1, 1, "lte(n): less than or equal", c => Bool(c.Number <= c.NumberArg(0)));
            Add("mt", 1, 1, "mt(n): more than", c => Bool(c.Number > c.NumberArg(0)));
            Add("mte", 1, 1, "mte(n): more than or equal", c => Bool(c.Number >= c.NumberArg(0)));
            Add("between", 2, 2, "between(min, max): inclusive", c => Bool(c.Number >= c.NumberArg(0) && c.Number <= c.NumberArg(1)));
            Add("is_zero", 0, 0, "yes if zero", c => Bool(c.Number == 0));
            Add("not_zero", 0, 0, "yes if not zero", c => Bool(c.Number != 0));
            Add("positive", 0, 0, "yes if zero or more", c => Bool(c.Number >= 0));
            Add("negative", 0, 0, "yes if less than zero", c => Bool(c.Number < 0));
            Add("increment", 0, 1, "increment(by?): on a variable, also updates it", c => Mutate(c, c.Number + (c.Args.Length > 0 ? c.NumberArg(0) : 1)));
            Add("decrement", 0, 1, "decrement(by?): on a variable, also updates it", c => Mutate(c, c.Number - (c.Args.Length > 0 ? c.NumberArg(0) : 1)));

            // ---- Bools ----
            Add("not", 0, 0, "negate", c => Bool(!c.Bool));
            Add("and", 1, 1, "and(other)", c => Bool(c.Bool & ValueTypes.ToBool(c.Args[0], c.Method)));
            Add("or", 1, 1, "or(other)", c => Bool(c.Bool | ValueTypes.ToBool(c.Args[0], c.Method)));
            Add("xor", 1, 1, "xor(other)", c => Bool(c.Bool ^ ValueTypes.ToBool(c.Args[0], c.Method)));
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static char PadChar(MethodCallContext c) => c.Args.Length > 1 && c.Args[1].Length > 0 ? c.Args[1][0] : ' ';

        private static string Mutate(MethodCallContext c, double value)
        {
            string result = Num(value);
            if (c.VariableName != null && c.Container != null)
            {
                c.Container.GetDef(c.VariableName).Contents = result;
            }
            return result;
        }

        /// <summary>Names and descriptions of the built-in methods.</summary>
        public static IEnumerable<KeyValuePair<string, string>> Describe() =>
            Methods.OrderBy(m => m.Key).Select(m => new KeyValuePair<string, string>(m.Key, m.Value.Description));

        public static bool Exists(string name) => Methods.ContainsKey(name);

        /// <summary>
        /// Invokes a built-in method. Returns false if there is no built-in with that name.
        /// </summary>
        internal static bool TryInvoke(MethodCallContext context, out string result)
        {
            result = null;

            if (!Methods.TryGetValue(context.Method, out Method method) || method.Body == null)
            {
                return false;
            }

            if (context.Args.Length < method.MinArgs || context.Args.Length > method.MaxArgs)
            {
                string expected = method.MinArgs == method.MaxArgs ? method.MinArgs.ToString() : $"{method.MinArgs} to {method.MaxArgs}";
                throw new KScriptException("Invalid_Method_Arguments",
                    $"'{context.Method}()' takes {expected} argument(s) but was given {context.Args.Length}. Usage: {method.Description}");
            }

            result = method.Body(context);
            return true;
        }
    }
}
