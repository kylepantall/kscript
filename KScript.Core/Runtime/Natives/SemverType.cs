using System.Globalization;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>semver</c>: a semantic version such as 1.4.2, 2.0.0-beta.1 or 1.0.0+build.5. Versions compare by SemVer 2.0.0
    /// precedence, not as text: 1.4.2 &lt; 1.10.0, a prerelease comes before its release (1.0.0-beta &lt; 1.0.0), and
    /// build metadata is ignored, so 1.4.2+build.5 == 1.4.2. Sorting a list of versions, and its ->max() and ->min(),
    /// use the same order.
    /// </summary>
    public sealed class SemverType : NativeType
    {
        public override string Name => "semver";

        public override string Description => "A semantic version such as 1.4.2, 2.0.0-beta.1 or 1.0.0+build.5. A leading v is dropped.";

        public override string Example => "1.4.2";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods { get; } = NativeTypes.Searchable.Concat(new[] { "split" }).ToArray();

        public override IReadOnlyList<string> Completions => new[] { "0.1.0", "1.0.0", "1.0.0-beta.1", "2.0.0" };

        // SemVer 2.0.0 section 10: build metadata MUST be ignored when determining precedence, so two versions that
        // differ only in it have the same precedence. == follows precedence, so it agrees with < and >.
        public override Equality Equality => Equality.By((a, b) => Compare((NativeText)a, (NativeText)b) == 0);

        public override Ordering Ordering => Ordering.By((a, b) => Compare((NativeText)a, (NativeText)b));

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
            static string Pre(object v) => (string)Part(v, "prerelease");
            static bool Tagged(object v, string tag) => Pre(v).Split('.')[0].StartsWith(tag, StringComparison.OrdinalIgnoreCase);
            const string Let = "<let name=\"x\" type=\"semver[]\" value=\"[1.0.0, 1.1.0-rc.1, 1.1.0-beta.2, 1.0.10]\" />\n";
            l.Filter("releases", "Versions without a prerelease: 1.0.0 but not 1.1.0-rc.1.", v => Pre(v).Length == 0)
             .Filter("prereleases", "Versions with a prerelease, such as 1.1.0-rc.1 or 2.0.0-beta.", v => Pre(v).Length > 0)
             .Filter("release_candidates", "Versions whose prerelease starts with rc: 1.1.0-rc.1.", v => Tagged(v, "rc"))
             .Filter("betas", "Versions whose prerelease starts with beta: 1.1.0-beta.2.", v => Tagged(v, "beta"))
             .Filter("alphas", "Versions whose prerelease starts with alpha: 2.0.0-alpha.", v => Tagged(v, "alpha"));
            l.WithItems("latest", "", "The highest release, leaving out prereleases (null when there is none). ->max() includes prereleases.", "semver",
                (items, c) => items.Where(v => Pre(Values.Unwrap(v)).Length == 0).OrderBy(Values.Unwrap, Comparer<object>.Create(Ordering.Compare.Invoke)).LastOrDefault(),
                Let + "<echo>{ $x->latest() }</echo>");
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            const string S = KType.String, N = KType.Number, B = KType.Bool;
            const string Let = "<let name=\"x\" type=\"semver\" value=\"'1.4.2-beta.1'\" />\n";
            m.Field("major", N, "The first number: 1 in 1.4.2.", (t, _) => Part(t, "major"), Let + "<echo>{ $x.major }</echo>");
            m.Field("minor", N, "The second number: 4 in 1.4.2.", (t, _) => Part(t, "minor"), Let + "<echo>{ $x.minor }</echo>");
            m.Field("patch", N, "The third number: 2 in 1.4.2.", (t, _) => Part(t, "patch"), Let + "<echo>{ $x.patch }</echo>");
            m.Field("prerelease", S, "What comes after -, such as beta.1, or '' for a release.", (t, _) => Part(t, "prerelease"), Let + "<echo>{ $x.prerelease }</echo>");
            m.Method("compare", "other: semver", "-1, 0 or 1 as this version comes before, with or after the other (build metadata is ignored).", N, (t, c) =>
                (double)Compare((NativeText)t, c.Arg(0) is NativeText o && o.TypeName == "semver" ? o : Require(c.Str(0))),
                Let + "<echo>{ $x->compare('1.4.2') }</echo>");
            m.Method("satisfies", "range: string", "True when the version is in the range: ^1.2.0, ~1.2, >=1.0.0 <2.0.0, 1.x, or several joined with ||.", B, (t, c) =>
            {
                bool ok = Satisfies((NativeText)t, c.Str(0), out string problem);
                if (problem != null) throw new KScriptException(problem + ".", "Value");
                return ok;
            }, Let + "<echo>{ $x->satisfies('^1.2.0') }</echo>");
            m.Method("bump", "part: string", "The next version: bump('major'), bump('minor') or bump('patch'). The prerelease is dropped.", "semver", (t, c) =>
            {
                double major = (double)Part(t, "major"), minor = (double)Part(t, "minor"), patch = (double)Part(t, "patch");
                string text = c.Str(0) switch
                {
                    "major" => $"{major + 1}.0.0",
                    "minor" => $"{major}.{minor + 1}.0",
                    "patch" => ((string)Part(t, "prerelease")).Length > 0 ? $"{major}.{minor}.{patch}" : $"{major}.{minor}.{patch + 1}",
                    string other => throw new KScriptException($"bump takes major, minor or patch, not '{other}'.", "Value"),
                };
                return Require(text);
            }, Let + "<echo>{ $x->bump('minor') }</echo>");
        }

        public override (object Value, string Reason) Parse(string s) => Read(s);

        private static object Part(object target, string name) => ((NativeText)target).Part(name);

        /// <summary>The semver this text means, or an error that says why it isn't one.</summary>
        private static NativeText Require(string text) => Read(text) is (NativeText v, _) ? v
            : throw new KScriptException($"'{text}' isn't {NativeTypes.Get("semver").WithArticle}: {Read(text).Reason}.", "Value");

        internal static (object Value, string Reason) Read(string s)
        {
            s = s.Trim();
            if (s.StartsWith('v') || s.StartsWith('V')) s = s.Substring(1);
            string build = null, pre = null;
            int plus = s.IndexOf('+');
            if (plus >= 0) { build = s.Substring(plus + 1); s = s.Substring(0, plus); }
            int dash = s.IndexOf('-');
            if (dash >= 0) { pre = s.Substring(dash + 1); s = s.Substring(0, dash); }
            string[] parts = s.Split('.');
            if (parts.Length != 3) return (null, "it needs three numbers, as in 1.4.2");
            var numbers = new List<double>();
            foreach (string p in parts)
            {
                if (p.Length == 0 || !p.All(char.IsAsciiDigit)) return (null, $"'{p}' isn't a number");
                if (p.Length > 1 && p[0] == '0') return (null, $"'{p}' can't start with 0");
                numbers.Add(double.Parse(p, CultureInfo.InvariantCulture));
            }
            foreach (string id in new[] { pre, build }.Where(x => x != null))
            {
                if (id.Split('.').Any(x => x.Length == 0 || x.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))) return (null, $"'{id}' must be dot-separated letters, digits and -");
            }
            string text = string.Join(".", parts) + (pre != null ? "-" + pre : string.Empty) + (build != null ? "+" + build : string.Empty);
            return (new NativeText("semver", text, new Dictionary<string, object>
            {
                ["major"] = numbers[0],
                ["minor"] = numbers[1],
                ["patch"] = numbers[2],
                ["prerelease"] = pre ?? string.Empty,
                ["build"] = build ?? string.Empty,
            }), null);
        }

        /// <summary>Compares two semvers by precedence (build metadata is ignored): -1, 0 or 1.</summary>
        internal static int Compare(NativeText a, NativeText b)
        {
            foreach (string part in new[] { "major", "minor", "patch" })
            {
                int c = ((double)a.Part(part)).CompareTo((double)b.Part(part));
                if (c != 0) return Math.Sign(c);
            }
            string pa = (string)a.Part("prerelease"), pb = (string)b.Part("prerelease");
            if (pa.Length == 0 || pb.Length == 0) return pa.Length == pb.Length ? 0 : pa.Length == 0 ? 1 : -1;
            string[] xa = pa.Split('.'), xb = pb.Split('.');
            for (int i = 0; i < Math.Min(xa.Length, xb.Length); i++)
            {
                bool na = long.TryParse(xa[i], out long ia), nb = long.TryParse(xb[i], out long ib);
                int c = na && nb ? ia.CompareTo(ib) : na ? -1 : nb ? 1 : string.CompareOrdinal(xa[i], xb[i]);
                if (c != 0) return Math.Sign(c);
            }
            return Math.Sign(xa.Length.CompareTo(xb.Length));
        }

        /// <summary>
        /// True if the version meets the range: ^1.2.0, ~1.2, &gt;=1.0.0 &lt;2.0.0, 1.x, 1.2.3, or several joined by ||.
        /// </summary>
        internal static bool Satisfies(NativeText version, string range, out string problem)
        {
            problem = null;
            foreach (string alternative in range.Split("||"))
            {
                bool all = true;
                foreach (string raw in alternative.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string op = new string(raw.TakeWhile(c => c is '^' or '~' or '>' or '<' or '=').ToArray());
                    string rest = raw.Substring(op.Length);
                    if (rest == "*" || rest.Length == 0 && op.Length == 0) continue;
                    string[] bits = rest.TrimStart('v').Split('.');
                    int given = Math.Min(3, bits.TakeWhile(b => b != "x" && b != "X" && b != "*").Count());
                    string full = string.Join(".", Enumerable.Range(0, 3).Select(i => i < given ? bits[i] : "0"));
                    if (Read(full) is not (NativeText low, null))
                    {
                        problem = $"'{raw}' isn't a version range part (use ^1.2.0, ~1.2, >=1.0.0, 1.x or 1.2.3)";
                        return false;
                    }
                    if (rest.Contains('-') && given == 3 && Read(rest) is (NativeText withPre, null)) low = withPre;
                    int cmp = Compare(version, low);
                    double major = (double)low.Part("major"), minor = (double)low.Part("minor"), patch = (double)low.Part("patch");
                    bool ok;
                    switch (op)
                    {
                        case "":
                        case "=":
                            ok = given == 3 ? cmp == 0 : (double)version.Part("major") == major && (given < 2 || (double)version.Part("minor") == minor);
                            break;
                        case ">": ok = cmp > 0; break;
                        case ">=": ok = cmp >= 0; break;
                        case "<": ok = cmp < 0; break;
                        case "<=": ok = cmp <= 0; break;
                        case "~":
                            ok = cmp >= 0 && (double)version.Part("major") == major && (given < 2 || (double)version.Part("minor") == minor);
                            break;
                        case "^":
                            ok = cmp >= 0 && (major > 0 ? (double)version.Part("major") == major
                                : minor > 0 || given < 3 ? (double)version.Part("major") == 0 && (given < 2 || (double)version.Part("minor") == minor)
                                : (double)version.Part("major") == 0 && (double)version.Part("minor") == 0 && (double)version.Part("patch") == patch);
                            break;
                        default:
                            problem = $"'{op}' isn't a version comparison (use ^, ~, >, >=, <, <= or =)";
                            return false;
                    }
                    if (!ok) all = false;
                }
                if (all) return true;
            }
            return false;
        }
    }
}
