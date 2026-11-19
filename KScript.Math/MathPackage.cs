using KScript.Metadata;
using KScript.Runtime;

using M = System.Math;

namespace KScript.Mathematics
{
    /// <summary>KScript.Math: roots, powers, trigonometry, logarithms, random numbers and statistics.</summary>
    public sealed class MathPackage : KScriptPackage
    {
        private static readonly Random Shared = new Random();
        private static Random seeded;

        public override string Name => "KScript.Math";

        public override string Description => "Roots, powers, trigonometry, logarithms, random numbers and statistics.";

        private static Random Rng => seeded ?? Shared;

        protected override void Register(PackageBuilder p)
        {
            p.Variable("pi", KType.Number, "π, about 3.14159.", (i, m) => M.PI);
            p.Variable("e", KType.Number, "e, about 2.71828.", (i, m) => M.E);
            p.Variable("tau", KType.Number, "τ (2π), about 6.28318.", (i, m) => M.Tau);

            One(p, "sqrt", "The square root.", M.Sqrt, "<echo>{ @sqrt(2) }</echo>");
            One(p, "cbrt", "The cube root.", M.Cbrt);
            One(p, "abs", "The number without its sign.", M.Abs);
            One(p, "sign", "-1, 0 or 1.", x => M.Sign(x));
            One(p, "exp", "e to the power of the number.", M.Exp);
            One(p, "sin", "The sine of an angle in radians.", M.Sin);
            One(p, "cos", "The cosine of an angle in radians.", M.Cos);
            One(p, "tan", "The tangent of an angle in radians.", M.Tan);
            One(p, "asin", "The angle in radians whose sine is the number.", M.Asin);
            One(p, "acos", "The angle in radians whose cosine is the number.", M.Acos);
            One(p, "atan", "The angle in radians whose tangent is the number.", M.Atan);
            One(p, "radians", "Degrees to radians.", x => x * M.PI / 180);
            One(p, "degrees", "Radians to degrees.", x => x * 180 / M.PI);
            One(p, "floor", "Rounded down.", M.Floor);
            One(p, "ceil", "Rounded up.", M.Ceiling);
            One(p, "trunc", "With the fraction removed.", M.Truncate);

            p.Command("pow", "base, exponent", "base to the power of exponent (the same as base ** exponent).", KType.Number, c => M.Pow(c.Num(0), c.Num(1)));
            p.Command("atan2", "y, x", "The angle in radians of the point (x, y).", KType.Number, c => M.Atan2(c.Num(0), c.Num(1)));
            p.Command("hypot", "x, y", "The length of the hypotenuse: sqrt(x² + y²).", KType.Number, c => M.Sqrt(c.Num(0) * c.Num(0) + c.Num(1) * c.Num(1)));
            p.Command("log", "number, base?", "The logarithm: natural (base e) unless a base is given.", KType.Number, c => c.Has(1) ? M.Log(c.Num(0), c.Num(1)) : M.Log(c.Num(0)));
            p.Command("log10", "number", "The base 10 logarithm.", KType.Number, c => M.Log10(c.Num(0)));
            p.Command("log2", "number", "The base 2 logarithm.", KType.Number, c => M.Log2(c.Num(0)));
            p.Command("round", "number, digits?", "Rounded to a number of decimal places (0 by default). Halves round away from zero.", KType.Number,
                c => M.Round(c.Num(0), M.Clamp(c.Int(1, 0), 0, 15), MidpointRounding.AwayFromZero));
            p.Command("clamp", "number, low, high", "The number kept between low and high.", KType.Number, c =>
            {
                double low = c.Num(1), high = c.Num(2);
                if (low > high) throw c.Error($"@clamp() needs low ({low}) to be at most high ({high}).", "Argument");
                return M.Clamp(c.Num(0), low, high);
            });
            p.Command("gcd", "a, b", "The greatest common divisor of two whole numbers.", KType.Number, c => (double)Gcd((long)c.Num(0), (long)c.Num(1)));
            p.Command("lcm", "a, b", "The least common multiple of two whole numbers.", KType.Number, c =>
            {
                long a = (long)c.Num(0), b = (long)c.Num(1);
                return a == 0 || b == 0 ? 0d : (double)M.Abs(a / Gcd(a, b) * b);
            });
            p.Command("factorial", "n", "n! for a whole number from 0 to 170.", KType.Number, c =>
            {
                int n = c.Int(0);
                if (n < 0 || n > 170) throw c.Error($"@factorial() needs a whole number from 0 to 170, but got {n}.", "Argument");
                double result = 1;
                for (int i = 2; i <= n; i++) result *= i;
                return result;
            });

            p.Command("random", "low?, high?", "A random number: from 0 up to 1 with no arguments, or a whole number from low to high (both included).", KType.Number, c =>
            {
                if (!c.Has(0)) return Rng.NextDouble();
                long low = c.Has(1) ? (long)c.Num(0) : 0, high = c.Has(1) ? (long)c.Num(1) : (long)c.Num(0);
                if (low > high) throw c.Error($"@random() needs low ({low}) to be at most high ({high}).", "Argument");
                return (double)Rng.NextInt64(low, high + 1);
            }, "<let name=\"roll\" value=\"@random(1, 6)\" />");
            p.Command("random_seed", "seed", "Makes @random(), @pick() and @shuffle() repeat the same sequence, for tests.", KType.Null, c =>
            {
                seeded = new Random(c.Int(0));
                return null;
            });
            p.Command("pick", "list", "A random item from a list.", KType.Any, c =>
            {
                List<object> list = c.List(0);
                if (list.Count == 0) throw c.Error("@pick() needs a list with at least one item.", "Argument");
                return list[Rng.Next(list.Count)];
            });
            p.Command("shuffle", "list", "A copy of a list in random order.", KType.List, c =>
            {
                var copy = new List<object>(c.List(0));
                for (int i = copy.Count - 1; i > 0; i--)
                {
                    int j = Rng.Next(i + 1);
                    (copy[i], copy[j]) = (copy[j], copy[i]);
                }
                return copy;
            });

            p.Command("sum", "values...", "The total of the numbers (or of one list).", KType.Number, c => Numbers(c).Sum());
            p.Command("average", "values...", "The mean of the numbers (or of one list).", KType.Number, c => NonEmpty(c).Average());
            p.Command("median", "values...", "The middle number (or the mean of the two middle numbers).", KType.Number, c => Median(NonEmpty(c)));
            p.Command("stdev", "values...", "The population standard deviation.", KType.Number, c => StdDev(NonEmpty(c)));

            p.Method(KType.List, "average", "", "The mean of the numbers in the list.", KType.Number, (t, c) => NonEmpty(c, (List<object>)t).Average());
            p.Method(KType.List, "median", "", "The middle number in the list.", KType.Number, (t, c) => Median(NonEmpty(c, (List<object>)t)));
            p.Method(KType.List, "stdev", "", "The population standard deviation of the list.", KType.Number, (t, c) => StdDev(NonEmpty(c, (List<object>)t)));
            p.Method(KType.Number, "sqrt", "", "The square root.", KType.Number, (t, c) => M.Sqrt(Values.ToNumber(t, "->sqrt()")));
            p.Method(KType.Number, "pow", "exponent", "The number to a power.", KType.Number, (t, c) => M.Pow(Values.ToNumber(t, "->pow()"), c.Num(0)));
        }

        private static void One(PackageBuilder p, string name, string description, Func<double, double> f, string example = null) =>
            p.Command(name, "number", description, KType.Number, c => f(c.Num(0)), example);

        private static long Gcd(long a, long b)
        {
            a = M.Abs(a);
            b = M.Abs(b);
            while (b != 0) (a, b) = (b, a % b);
            return a;
        }

        private static List<double> Numbers(CallContext c, List<object> list = null)
        {
            IEnumerable<object> items = list ?? (c.Count == 1 && c.Arg(0) is List<object> one ? one : c.Args);
            return items.Select((x, i) => Values.ToNumber(x, $"Item {i + 1} of {c.Display}")).ToList();
        }

        private static List<double> NonEmpty(CallContext c, List<object> list = null)
        {
            List<double> numbers = Numbers(c, list);
            if (numbers.Count == 0) throw c.Error($"{c.Display} needs at least one number.", "Argument");
            return numbers;
        }

        private static double Median(List<double> numbers)
        {
            numbers.Sort();
            int mid = numbers.Count / 2;
            return numbers.Count % 2 == 1 ? numbers[mid] : (numbers[mid - 1] + numbers[mid]) / 2;
        }

        private static double StdDev(List<double> numbers)
        {
            double mean = numbers.Average();
            return M.Sqrt(numbers.Sum(x => (x - mean) * (x - mean)) / numbers.Count);
        }
    }
}
