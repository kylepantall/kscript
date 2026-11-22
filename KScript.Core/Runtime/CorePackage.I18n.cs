using System.Globalization;

using KScript.Metadata;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Runtime
{
    public sealed partial class CorePackage
    {
        /// <summary>Translations and locale formatting: @t(), @locale(), &lt;locale&gt;, &lt;translations&gt;, @format_*.</summary>
        private static void RegisterI18n(PackageBuilder p)
        {
            p.Command("t", "key, values?, locale?",
                "The translation of a key in the current locale (from locales/fr.json and so on), with {name} filled in from values. With values.count, plural forms (one, other, few...) are picked for the language.",
                KType.String, c =>
                {
                    Translator tr = TranslatorFor(c.Interpreter, c.Module);
                    object values = c.Arg(1, "values");
                    if (values != null && values is not Dictionary<string, object>)
                        throw new KScriptException($"@t() values should be a map, such as {{ name: $user.name, count: 3 }}, but got {Values.Describe(values)}.", "Argument");
                    return tr.Translate(c.Str(0), values as Dictionary<string, object>, c.Arg(2, "locale") is object l ? Values.ToStr(l) : null);
                }, "<let name=\"items\" value=\"[1, 2, 3]\" />\n<echo>{ @t('cart.items', { count: $items->count() }) }</echo>");
            p.Command("has_t", "key, locale?", "True when a key has a translation in the current locale (or its fallbacks).", KType.Bool,
                c => TranslatorFor(c.Interpreter, c.Module).Has(c.Str(0), c.Arg(1, "locale") is object l ? Values.ToStr(l) : null));
            p.Command("locale", "", "The current locale, such as fr or en-GB.", KType.String, c => TranslatorFor(c.Interpreter, c.Module).Locale);
            p.Command("locales", "", "The locales there are translation files for.", KType.List, c => TranslatorFor(c.Interpreter, c.Module).Available.Cast<object>().ToList());
            p.Command("plural", "count, locale?", "The plural category of a number in a language: zero, one, two, few, many or other.", KType.String,
                c => Translator.Plural(c.Arg(1, "locale") is object l ? Values.ToStr(l) : TranslatorFor(c.Interpreter, c.Module).Locale, c.Num(0)));

            p.Command("locale_path", "path, locale?",
                "A site path for a locale, for language links: @locale_path('/about', 'fr') is /fr/about. The default locale has no prefix, and a locale prefix already on the path is replaced.",
                KType.String, c =>
                {
                    Translator tr = TranslatorFor(c.Interpreter, c.Module);
                    string target = c.Arg(1, "locale") is object l ? Translator.Normalise(Values.ToStr(l)) : tr.Locale;
                    var segments = c.Str(0).Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (segments.Count > 0 && tr.Available.Any(a => a.Equals(segments[0], StringComparison.OrdinalIgnoreCase))) segments.RemoveAt(0);
                    if (!target.Equals(tr.Default, StringComparison.OrdinalIgnoreCase)) segments.Insert(0, target);
                    return "/" + string.Join("/", segments);
                }, "<a href=\"{ @locale_path($request.path, 'fr') }\">Français</a>");

            p.Element(new ElementInfo("locale", "Changes the current locale, which @t() and the @format_ commands use. Without `set`, picks the best available locale from `from` (a list of wishes, such as an Accept-Language header).", ContentKind.Empty,
                new A("set", K.Template, "The locale, such as fr or en-GB."),
                new A("from", K.Expression, "Wishes in order, as a list or an Accept-Language header: the best available locale is used."))
            {
                Example = "<locale set=\"fr\" />\n<locale from=\"['fr-CA', 'en']\" />",
            }, run =>
            {
                Translator tr = TranslatorFor(run.Interpreter, run.Module);
                if (run.Has("set")) tr.Locale = run.Template("set");
                else if (run.Has("from"))
                {
                    object from = run.Eval("from");
                    IEnumerable<string> wishes = from is List<object> list ? list.Select(Values.ToStr) : Translator.AcceptLanguage(Values.ToStr(from));
                    tr.Locale = tr.Best(wishes) ?? tr.Default;
                }
                else throw run.Error($"<locale> needs set=\"fr\" or from=\"...\".", null, "Syntax");
                return null;
            });

            p.Element(new ElementInfo("translations", "Says where translation files are and which locale to fall back to. Usually set in config under i18n instead.", ContentKind.Empty,
                new A("folder", K.Template, "The folder of translation files (default locales): locales/en.json, locales/fr.yaml, or locales/fr/*.json."),
                new A("default", K.Template, "The locale used when a translation is missing (default en)."))
            {
                Example = "<translations folder=\"lang\" default=\"en-GB\" />",
            }, run =>
            {
                Translator tr = TranslatorFor(run.Interpreter, run.Module);
                tr.Configure(run.Has("folder") ? run.Template("folder") : null, run.Has("default") ? run.Template("default") : null);
                return null;
            });

            p.Command("format_number", "number, decimals?, locale?", "A number written for the locale: 1,234.5 in en, 1 234,5 in fr.", KType.String,
                c => Formats.Number(c.Num(0), c.Has(1, "decimals") && c.Arg(1, "decimals") != null ? c.Int(1) : null, LocaleArg(c, 2)));
            p.Command("format_percent", "fraction, decimals?, locale?", "A fraction as a percentage for the locale: 0.25 is 25%.", KType.String,
                c => Formats.Percent(c.Num(0), c.Int(1, 0), LocaleArg(c, 2)));
            p.Command("format_currency", "amount, currency, locale?", "An amount of money for the locale: @format_currency(9.5, 'EUR') is €9.50 in en and 9,50 € in fr.", KType.String,
                c => Formats.Currency(c.Num(0), c.Str(1), LocaleArg(c, 2)));
            p.Command("format_date", "date, style?, locale?", "A date for the locale. style: short, medium (the default), long, full, time, datetime, or a pattern such as 'd MMMM yyyy'.", KType.String,
                c => Formats.Date(DateArg(c.Need(0)), c.Arg(1, "style") is object s ? Values.ToStr(s) : null, LocaleArg(c, 2)));
        }

        private static string LocaleArg(CallContext c, int i) =>
            c.Arg(i, "locale") is object l ? Translator.Normalise(Values.ToStr(l)) : TranslatorFor(c.Interpreter, c.Module).Locale;

        private static DateTime DateArg(object value)
        {
            if (value is DateTime d) return d;
            if (value is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTime parsed)) return parsed;
            throw new KScriptException($"@format_date() needs a date, such as @now() or '2026-11-20', but got {Values.Describe(value)}.", "Argument");
        }

        private static Translator TranslatorFor(Interpreter interpreter, Module module)
        {
            Translator tr = Translator.For(interpreter.Host, module?.Directory ?? interpreter.Host.WorkingDirectory);
            tr.Missing ??= (key, locale) => new LoggerValue(interpreter, module, "i18n").Log(LogLevel.Warn, $"Missing translation '{key}' for {locale}", null);
            return tr;
        }
    }
}
