using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    public sealed partial class CorePackage
    {
        private static NativeText T(object target) => (NativeText)target;

        private static object Part(object target, string name) => T(target).Part(name);

        /// <summary>Makes a native value from text, or fails with why it doesn't fit.</summary>
        private static object Native(string type, string text)
        {
            if (SemanticTypes.TryConvert(type, text, out object value, out string reason)) return value;
            throw new KScriptException($"'{text}' isn't {NativeTypes.Get(type).WithArticle}: {reason}.", "Value");
        }

        /// <summary>The methods of url, ip, country and the other native types that have no class of their own.</summary>
        private static void RegisterNativeMethods(PackageBuilder p)
        {
            const string S = KType.String, N = KType.Number;

            // The types on the NativeType contract declare their own fields and methods.
            foreach (NativeType type in NativeTypeRegistry.BuiltIn) type.Members(p);
            ListFilters.Register(p);

            // phone, country, currency, locale, timezone
            p.Field("phone", "country_code", S, "The country calling code without +: 44 for a UK number.", (t, _) => Part(t, "country_code"));
            p.Field("phone", "national", S, "The number after the country code.", (t, _) => Part(t, "national"));
            p.Method("phone", "tel", "", "A tel: link for it: tel:+447700900123.", S, (t, c) => "tel:" + T(t).Text);
            p.Field("country", "name", S, "The country's name in English: United Kingdom.", (t, _) => Part(t, "name"));
            p.Field("country", "alpha3", S, "The three-letter code: GBR.", (t, _) => Part(t, "alpha3"));
            p.Field("country", "currency", "currency", "The currency the country uses: GBP.", (t, _) => Native("currency", (string)Part(t, "currency")));
            p.Field("country", "flag", S, "The country's flag emoji: 🇬🇧.", (t, _) => string.Concat(T(t).Text.Select(ch => char.ConvertFromUtf32(0x1F1E6 + ch - 'A'))));
            p.Field("currency", "name", S, "The currency's name in English: British Pound.", (t, _) => Part(t, "name"));
            p.Field("currency", "symbol", S, "Its symbol: £.", (t, _) => Part(t, "symbol"));
            p.Field("currency", "digits", N, "How many decimal places amounts have: 2 for GBP, 0 for JPY.", (t, _) => Part(t, "digits"));
            p.Method("currency", "format", "amount: number", "An amount written in this currency: £1,234.50.", S, (t, c) =>
            {
                int digits = (int)(double)Part(t, "digits");
                string number = Math.Abs(c.Num(0)).ToString("N" + digits, CultureInfo.InvariantCulture);
                return (c.Num(0) < 0 ? "-" : string.Empty) + (string)Part(t, "symbol") + number;
            });
            p.Field("locale", "language", S, "The language code: en in en-GB.", (t, _) => Part(t, "language"));
            p.Field("locale", "region", S, "The region code: GB in en-GB, or '' when there isn't one.", (t, _) => Part(t, "region"));
            p.Field("locale", "name", S, "Its name in English: English (United Kingdom).", (t, _) => Part(t, "name"));
            p.Field("locale", "native_name", S, "Its name in its own language: français (France).", (t, _) => Part(t, "native_name"));
            p.Method("locale", "format", "value: any", "A number or date written the way this locale writes it: 1.234,5 in de-DE.", S, (t, c) =>
            {
                CultureInfo culture = (CultureInfo)Part(t, "culture");
                return c.Arg(0) switch
                {
                    double d => d.ToString("#,0.##########", culture),
                    DateTime dt => dt.ToString("d", culture),
                    object o => Values.ToStr(o),
                    null => string.Empty,
                };
            });
            // iban, card
            p.Field("iban", "country", "country", "The country the account is in: GB.", (t, _) => Native("country", (string)Part(t, "country")));
            p.Method("iban", "formatted", "", "The IBAN in groups of four, the way it is printed: GB82 WEST 1234 5698 7654 32.", S, (t, c) => Part(t, "formatted"));
            p.Field("iban", "bban", S, "The account number inside it, after the country code and check digits.", (t, _) => Part(t, "bban"));
            p.Field("card", "last4", S, "The last four digits, which are safe to show.", (t, _) => Part(t, "last4"));
            p.Field("card", "brand", S, "visa, mastercard, amex, discover, jcb, diners, unionpay, maestro or unknown, from its first digits.", (t, _) => Part(t, "brand"));
            p.Method("card", "reveal", "", "The whole number. Only use it to pass the card on to a payment provider; never store or log it.", S, (t, c) => T(t).Text);

            p.Examples(
                ("phone.country_code", "<let name=\"x\" type=\"phone\" value=\"'+44 7700 900123'\" />\n<echo>{ $x.country_code }</echo>"),
                ("phone.national", "<let name=\"x\" type=\"phone\" value=\"'+44 7700 900123'\" />\n<echo>{ $x.national }</echo>"),
                ("phone->tel", "<let name=\"x\" type=\"phone\" value=\"'+44 7700 900123'\" />\n<echo>{ $x->tel() }</echo>"),
                ("country.name", "<let name=\"x\" type=\"country\" value=\"'United Kingdom'\" />\n<echo>{ $x.name }</echo>"),
                ("country.alpha3", "<let name=\"x\" type=\"country\" value=\"'United Kingdom'\" />\n<echo>{ $x.alpha3 }</echo>"),
                ("country.currency", "<let name=\"x\" type=\"country\" value=\"'United Kingdom'\" />\n<echo>{ $x.currency }</echo>"),
                ("country.flag", "<let name=\"x\" type=\"country\" value=\"'United Kingdom'\" />\n<echo>{ $x.flag }</echo>"),
                ("currency.name", "<let name=\"x\" type=\"currency\" value=\"'jpy'\" />\n<echo>{ $x.name }</echo>"),
                ("currency.symbol", "<let name=\"x\" type=\"currency\" value=\"'jpy'\" />\n<echo>{ $x.symbol }</echo>"),
                ("currency.digits", "<let name=\"x\" type=\"currency\" value=\"'jpy'\" />\n<echo>{ $x.digits }</echo>"),
                ("currency->format", "<let name=\"x\" type=\"currency\" value=\"'jpy'\" />\n<echo>{ $x->format(1234.5) }</echo>"),
                ("locale.language", "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n<echo>{ $x.language }</echo>"),
                ("locale.region", "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n<echo>{ $x.region }</echo>"),
                ("locale.name", "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n<echo>{ $x.name }</echo>"),
                ("locale.native_name", "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n<echo>{ $x.native_name }</echo>"),
                ("locale->format", "<let name=\"x\" type=\"locale\" value=\"'de-DE'\" />\n<echo>{ $x->format(1234.5) }</echo>"),
                ("iban.country", "<let name=\"x\" type=\"iban\" value=\"'GB82 WEST 1234 5698 7654 32'\" />\n<echo>{ $x.country }</echo>"),
                ("iban->formatted", "<let name=\"x\" type=\"iban\" value=\"'GB82 WEST 1234 5698 7654 32'\" />\n<echo>{ $x->formatted() }</echo>"),
                ("iban.bban", "<let name=\"x\" type=\"iban\" value=\"'GB82 WEST 1234 5698 7654 32'\" />\n<echo>{ $x.bban }</echo>"),
                ("card.last4", "<let name=\"x\" type=\"card\" value=\"'4242 4242 4242 4242'\" />\n<echo>{ $x.last4 }</echo>"),
                ("card.brand", "<let name=\"x\" type=\"card\" value=\"'4242 4242 4242 4242'\" />\n<echo>{ $x.brand }</echo>"),
                ("card->reveal", "<let name=\"x\" type=\"card\" value=\"'4242 4242 4242 4242'\" />\n<echo>{ $x->reveal()->count() }</echo>"));
        }
    }
}
