using System.Globalization;
using System.Text.RegularExpressions;

namespace KScript.Runtime
{
    /// <summary>
    /// A run's translations: <c>locales/en.json</c>, <c>locales/fr.yaml</c> or <c>locales/fr/*.json</c>, the current
    /// locale, and the fallbacks between them (fr-CA, then fr, then the default locale, then the key itself).
    /// Settings come from <c>$config.i18n</c>: <c>{ folder: 'locales', default: 'en', locale: 'fr' }</c>.
    /// </summary>
    public sealed class Translator
    {
        /// <summary>Where a page puts its $request, so the locale can come from it.</summary>
        public const string RequestKey = "KScript.I18n.Request";
        private static readonly string[] Extensions = { ".json", ".yaml", ".yml" };

        private readonly KScriptHost host;
        private readonly string directory;
        private readonly Dictionary<string, Dictionary<string, object>> loaded = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        private List<string> available;
        private string locale;

        public Translator(KScriptHost host, string directory, string folder = null, string defaultLocale = null)
        {
            this.host = host;
            this.directory = directory;
            Folder = folder ?? "locales";
            Default = Normalise(defaultLocale) ?? "en";
        }

        /// <summary>The folder of translation files, relative to the script (or the site folder).</summary>
        public string Folder { get; private set; }

        /// <summary>The locale used when a translation is missing in the current one.</summary>
        public string Default { get; private set; }

        /// <summary>Called with a key and locale when a translation is missing everywhere, once per key and locale.</summary>
        public Action<string, string> Missing { get; set; }

        /// <summary>The run's translator, made on first use from $config.i18n (and, in pages, the request).</summary>
        public static Translator For(KScriptHost host, string directory)
        {
            lock (host.Items)
            {
                if (host.Items.TryGetValue(nameof(Translator), out object existing) && existing is Translator t) return t;
            }
            Dictionary<string, object> settings = ConfigValue.For(host, directory).Values.TryGetValue("i18n", out object s) ? s as Dictionary<string, object> : null;
            if (settings == null && s != null) throw new KScriptException("i18n should be a map, such as { folder: 'locales', default: 'en' }.", "Config");
            var created = new Translator(host, directory, Text(settings, "folder"), Text(settings, "default"));
            string chosen = Text(settings, "locale");
            lock (host.Items)
            {
                if (host.Items.TryGetValue(nameof(Translator), out object raced) && raced is Translator r) return r;
                host.Items[nameof(Translator)] = created;
            }
            if (host.Items.TryGetValue(RequestKey, out object request) && request is Dictionary<string, object> req) chosen = created.FromRequest(req) ?? chosen;
            created.Locale = chosen ?? System.Environment.GetEnvironmentVariable("KSCRIPT_LOCALE") ?? created.Best(new[] { CultureInfo.CurrentUICulture.Name }) ?? created.Default;
            return created;
        }

        private static string Text(Dictionary<string, object> map, string key) => map != null && map.TryGetValue(key, out object v) && v != null ? Values.ToStr(v) : null;

        /// <summary>Changes where translations come from (the &lt;translations&gt; element).</summary>
        public void Configure(string folder, string defaultLocale)
        {
            if (folder != null) Folder = folder;
            if (defaultLocale != null) Default = Normalise(defaultLocale);
            loaded.Clear();
            available = null;
        }

        /// <summary>The current locale, such as fr or en-GB.</summary>
        public string Locale
        {
            get => locale ?? Default;
            set => locale = Normalise(value);
        }

        /// <summary>en_gb and EN-gb are en-GB.</summary>
        public static string Normalise(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string[] parts = name.Trim().Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !parts.All(p => p.All(char.IsLetterOrDigit)))
                throw new KScriptException($"'{name}' isn't a locale. Use a language code such as en, fr or en-GB.", "Argument");
            return string.Join("-", parts.Select((p, i) => i == 0 ? p.ToLowerInvariant() : p.Length == 2 ? p.ToUpperInvariant() : p));
        }

        private string Root => host.ResolvePath(Folder, directory);

        /// <summary>The locales there are translation files for, in order.</summary>
        public IReadOnlyList<string> Available
        {
            get
            {
                if (available != null) return available;
                var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                string root = Root;
                if (Directory.Exists(root))
                {
                    foreach (string file in Directory.EnumerateFiles(root).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
                        found.Add(Path.GetFileNameWithoutExtension(file));
                    foreach (string folder in Directory.EnumerateDirectories(root)) found.Add(Path.GetFileName(folder));
                }
                available = found.Select(f => TryNormalise(f)).Where(f => f != null).Distinct().ToList();
                return available;
            }
        }

        private static string TryNormalise(string name)
        {
            try
            {
                return Normalise(name);
            }
            catch (KScriptException)
            {
                return null;
            }
        }

        /// <summary>The best available locale for a list of wishes (fr-CA matches fr), or null.</summary>
        public string Best(IEnumerable<string> wishes)
        {
            IReadOnlyList<string> have = Available;
            foreach (string wish in wishes)
            {
                string w = TryNormalise(wish);
                if (w == null) continue;
                string exact = have.FirstOrDefault(h => h.Equals(w, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                string language = w.Split('-')[0];
                string near = have.FirstOrDefault(h => h.Equals(language, StringComparison.OrdinalIgnoreCase))
                    ?? have.FirstOrDefault(h => h.Split('-')[0].Equals(language, StringComparison.OrdinalIgnoreCase));
                if (near != null) return near;
            }
            return null;
        }

        /// <summary>
        /// A page's locale: the /fr/ path prefix, then ?lang=, then the lang cookie, then the browser's
        /// Accept-Language. Null when none of them names an available locale.
        /// </summary>
        public string FromRequest(Dictionary<string, object> request)
        {
            var wishes = new List<string>();
            if (request.TryGetValue("locale", out object l) && l is string fromPath) wishes.Add(fromPath);
            if (request.TryGetValue("query", out object q) && q is Dictionary<string, object> query && query.TryGetValue("lang", out object lang)) wishes.Add(Values.ToStr(lang));
            if (request.TryGetValue("cookies", out object c) && c is Dictionary<string, object> cookies && cookies.TryGetValue("lang", out object cookie)) wishes.Add(Values.ToStr(cookie));
            if (request.TryGetValue("headers", out object h) && h is Dictionary<string, object> headers && headers.TryGetValue("accept-language", out object accept))
                wishes.AddRange(AcceptLanguage(Values.ToStr(accept)));
            return Best(wishes);
        }

        /// <summary>The languages in an Accept-Language header, most wanted first: "fr-CH, fr;q=0.9, en;q=0.8".</summary>
        public static List<string> AcceptLanguage(string header) =>
            (header ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((part, index) =>
                {
                    string[] bits = part.Split(';', StringSplitOptions.TrimEntries);
                    double weight = 1;
                    foreach (string bit in bits.Skip(1))
                    {
                        if (bit.StartsWith("q=", StringComparison.OrdinalIgnoreCase)) double.TryParse(bit.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out weight);
                    }
                    return (Name: bits[0], Weight: weight, Index: index);
                })
                .Where(x => x.Name != "*" && x.Weight > 0)
                .OrderByDescending(x => x.Weight).ThenBy(x => x.Index)
                .Select(x => x.Name).ToList();

        /// <summary>The locales tried for <paramref name="wanted"/>, in order: fr-CA, fr, then the default.</summary>
        public List<string> Chain(string wanted)
        {
            var chain = new List<string>();
            string w = Normalise(wanted) ?? Default;
            chain.Add(w);
            if (w.Contains('-')) chain.Add(w.Split('-')[0]);
            chain.Add(Default);
            if (Default.Contains('-')) chain.Add(Default.Split('-')[0]);
            return chain.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>A locale's translations: locales/fr.json (or .yaml), merged with locales/fr/*.json under their file names.</summary>
        public Dictionary<string, object> Strings(string name)
        {
            if (loaded.TryGetValue(name, out var strings)) return strings;
            strings = ConfigLoader.NewMap();
            string root = Root;
            foreach (string ext in Extensions)
            {
                string file = Path.Combine(root, name + ext);
                if (File.Exists(file)) ConfigLoader.Merge(strings, Read(file));
            }
            string folder = Path.Combine(root, name);
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.EnumerateFiles(folder).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var part = ConfigLoader.NewMap();
                    part[Path.GetFileNameWithoutExtension(file)] = Read(file);
                    ConfigLoader.Merge(strings, part);
                }
            }
            loaded[name] = strings;
            return strings;
        }

        private Dictionary<string, object> Read(string file)
        {
            host.CheckPath(file);
            string text = File.ReadAllText(file);
            object value = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? Values.ParseJson(text) : Yaml.Parse(text, Path.GetFileName(file));
            if (value == null) return ConfigLoader.NewMap();
            if (value is not Dictionary<string, object> map)
                throw new KScriptException($"{Path.GetFileName(file)} should hold translations by key (a map), but holds {Values.Describe(value)}.", "I18n");
            var copy = ConfigLoader.NewMap();
            ConfigLoader.Merge(copy, map);
            return copy;
        }

        /// <summary>The translation of a key, with {name} filled in from <paramref name="values"/> and plurals picked by count.</summary>
        public string Translate(string key, Dictionary<string, object> values, string wanted = null)
        {
            values ??= new Dictionary<string, object>();
            string target = Normalise(wanted) ?? Locale;
            foreach (string name in Chain(target))
            {
                if (!ConfigLoader.TryGet(Strings(name), key, out object found) || found == null) continue;
                string text = Pick(found, values, name, key);
                if (text != null) return Fill(text, values, target);
            }
            if (reported.Add(target + "|" + key)) Missing?.Invoke(key, target);
            return Fill(key, values, target);
        }

        /// <summary>True when a key has a translation in the locale or its fallbacks.</summary>
        public bool Has(string key, string wanted = null) =>
            Chain(Normalise(wanted) ?? Locale).Any(name => ConfigLoader.TryGet(Strings(name), key, out object found) && found != null);

        private static readonly string[] Categories = { "zero", "one", "two", "few", "many", "other" };

        /// <summary>Text, or for a map of plural forms ({ one, other, =0 }) the form for values.count.</summary>
        private static string Pick(object found, Dictionary<string, object> values, string locale, string key)
        {
            if (found is string s) return s;
            if (found is not Dictionary<string, object> forms || !forms.Keys.Any(k => Categories.Contains(k, StringComparer.OrdinalIgnoreCase) || k.StartsWith('=')))
                return found is Dictionary<string, object> || found is List<object> ? null : Values.ToStr(found);
            if (!values.TryGetValue("count", out object count) || !Values.TryNumber(count, out double n))
                throw new KScriptException($"The translation '{key}' has plural forms, so it needs a count: @t('{key}', {{ count: $n }}).", "I18n");
            string exact = "=" + n.ToString(CultureInfo.InvariantCulture);
            if (forms.TryGetValue(exact, out object e)) return Values.ToStr(e);
            string category = Plural(locale, n);
            if (forms.TryGetValue(category, out object form)) return Values.ToStr(form);
            return forms.TryGetValue("other", out object other) ? Values.ToStr(other) : null;
        }

        private static readonly Regex Placeholder = new Regex(@"\{\s*([A-Za-z_][A-Za-z0-9_.]*)\s*\}");

        /// <summary>Puts values into {name} placeholders. Numbers are formatted for the locale.</summary>
        private static string Fill(string text, Dictionary<string, object> values, string locale) =>
            Placeholder.Replace(text, m => ConfigLoader.TryGet(values, m.Groups[1].Value, out object v) && v != null
                ? (v is double d ? Formats.Number(d, null, locale) : Values.ToStr(v))
                : m.Value);

        /// <summary>
        /// The CLDR plural category of <paramref name="n"/> in a language: zero, one, two, few, many or other.
        /// Covers the common rules; languages not listed use one (n = 1) and other.
        /// </summary>
        public static string Plural(string locale, double n)
        {
            string lang = (Normalise(locale) ?? "en").Split('-')[0];
            bool whole = n == Math.Floor(n);
            long i = (long)Math.Floor(Math.Abs(n));
            long mod10 = i % 10, mod100 = i % 100;
            switch (lang)
            {
                case "ja": case "zh": case "ko": case "th": case "vi": case "id": case "ms": case "lo": case "my":
                    return "other";
                case "fr": case "hy": case "kab":
                    return i == 0 || i == 1 ? "one" : "other";
                case "pt" when locale != null && !locale.StartsWith("pt-PT", StringComparison.OrdinalIgnoreCase):
                    return i == 0 || i == 1 ? "one" : "other";
                case "ru": case "uk": case "be": case "sr": case "hr": case "bs":
                    if (!whole) return "other";
                    if (mod10 == 1 && mod100 != 11) return "one";
                    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "few";
                    return "many";
                case "pl":
                    if (!whole) return "other";
                    if (i == 1) return "one";
                    if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return "few";
                    return "many";
                case "cs": case "sk":
                    if (!whole) return "many";
                    return i == 1 ? "one" : i >= 2 && i <= 4 ? "few" : "other";
                case "ar":
                    if (!whole) return "other";
                    if (i == 0) return "zero";
                    if (i == 1) return "one";
                    if (i == 2) return "two";
                    if (mod100 >= 3 && mod100 <= 10) return "few";
                    if (mod100 >= 11) return "many";
                    return "other";
                case "he":
                    if (!whole) return "other";
                    return i == 1 ? "one" : i == 2 ? "two" : "other";
                case "ga":
                    if (!whole) return "other";
                    return i == 1 ? "one" : i == 2 ? "two" : i >= 3 && i <= 6 ? "few" : i >= 7 && i <= 10 ? "many" : "other";
                case "cy":
                    if (!whole) return "other";
                    return i == 0 ? "zero" : i == 1 ? "one" : i == 2 ? "two" : i == 3 ? "few" : i == 6 ? "many" : "other";
                default:
                    return whole && i == 1 ? "one" : "other";
            }
        }
    }

    /// <summary>Numbers, money, percentages and dates written the way a locale writes them.</summary>
    public static class Formats
    {
        public static CultureInfo Culture(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return CultureInfo.InvariantCulture;
            try
            {
                return CultureInfo.GetCultureInfo(locale);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }

        /// <summary>1234.5 is 1,234.5 in en and 1 234,5 in fr. With decimals, exactly that many.</summary>
        public static string Number(double n, int? decimals, string locale) =>
            n.ToString(decimals.HasValue ? "N" + Math.Clamp(decimals.Value, 0, 15) : "#,0.##########", Culture(locale));

        public static string Percent(double fraction, int decimals, string locale) => fraction.ToString("P" + Math.Clamp(decimals, 0, 15), Culture(locale));

        private static readonly Dictionary<string, (string Symbol, int Decimals)> Currencies = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = ("$", 2), ["EUR"] = ("€", 2), ["GBP"] = ("£", 2), ["JPY"] = ("¥", 0), ["CNY"] = ("¥", 2), ["INR"] = ("₹", 2),
            ["CHF"] = ("CHF", 2), ["CAD"] = ("CA$", 2), ["AUD"] = ("A$", 2), ["KRW"] = ("₩", 0), ["SEK"] = ("kr", 2), ["NOK"] = ("kr", 2),
            ["DKK"] = ("kr.", 2), ["PLN"] = ("zł", 2), ["BRL"] = ("R$", 2), ["MXN"] = ("MX$", 2), ["RUB"] = ("₽", 2), ["TRY"] = ("₺", 2),
        };

        /// <summary>An amount of money in a currency (ISO code such as EUR), written for the locale: €1,234.50 or 1 234,50 €.</summary>
        public static string Currency(double amount, string code, string locale)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 3 || !code.Trim().All(char.IsLetter))
                throw new KScriptException($"'{code}' isn't a currency code. Use three letters, such as EUR, USD or GBP.", "Argument");
            var format = (NumberFormatInfo)Culture(locale).NumberFormat.Clone();
            (string symbol, int decimals) = Currencies.TryGetValue(code.Trim(), out var known) ? known : (code.Trim().ToUpperInvariant(), 2);
            format.CurrencySymbol = symbol;
            format.CurrencyDecimalDigits = decimals;
            return amount.ToString("C", format);
        }

        /// <summary>
        /// A date for the locale: short (1/2/2026), medium (Jan 2, 2026), long (January 2, 2026), full (with the
        /// weekday), time, datetime, or a pattern such as 'd MMMM yyyy'.
        /// </summary>
        public static string Date(DateTime date, string style, string locale)
        {
            CultureInfo culture = Culture(locale);
            DateTimeFormatInfo f = culture.DateTimeFormat;
            string pattern = (style ?? "medium").ToLowerInvariant() switch
            {
                "short" => f.ShortDatePattern,
                "medium" => MediumPattern(f),
                "long" => f.LongDatePattern.Replace("dddd, ", string.Empty).Replace("dddd ", string.Empty),
                "full" => f.LongDatePattern,
                "time" => f.ShortTimePattern,
                "datetime" => f.ShortDatePattern + " " + f.ShortTimePattern,
                _ => style,
            };
            return date.ToString(pattern, culture);
        }

        private static string MediumPattern(DateTimeFormatInfo f)
        {
            string longDate = f.LongDatePattern.Replace("dddd, ", string.Empty).Replace("dddd ", string.Empty);
            return Regex.Replace(longDate, "M{4}", "MMM");
        }
    }
}
