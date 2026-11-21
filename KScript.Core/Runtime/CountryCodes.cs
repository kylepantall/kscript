using System.Globalization;

namespace KScript.Runtime
{
    /// <summary>Countries, currencies and phone calling codes, for the country, currency and phone native types.</summary>
    internal static class CountryCodes
    {
        internal sealed class CurrencyInfo
        {
            public string Name;
            public string Symbol;
            public int Digits;
        }

        private static readonly Lazy<(Dictionary<string, RegionInfo> ByKey, Dictionary<string, CurrencyInfo> Currencies)> Data = new(Load);

        private static (Dictionary<string, RegionInfo>, Dictionary<string, CurrencyInfo>) Load()
        {
            var regions = new Dictionary<string, RegionInfo>(StringComparer.OrdinalIgnoreCase);
            var currencies = new Dictionary<string, CurrencyInfo>(StringComparer.Ordinal);
            foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                RegionInfo region;
                try
                {
                    region = new RegionInfo(culture.Name);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                string two = region.TwoLetterISORegionName;
                if (two.Length != 2 || !two.All(char.IsAsciiLetter)) continue;
                regions.TryAdd(two, region);
                if (region.ThreeLetterISORegionName.Length == 3) regions.TryAdd(region.ThreeLetterISORegionName, region);
                regions.TryAdd(region.EnglishName, region);
                string code = region.ISOCurrencySymbol;
                if (code.Length == 3 && !currencies.ContainsKey(code))
                {
                    currencies[code] = new CurrencyInfo
                    {
                        Name = region.CurrencyEnglishName,
                        Symbol = region.CurrencySymbol,
                        Digits = culture.NumberFormat.CurrencyDecimalDigits,
                    };
                }
            }
            // Currencies with no country of their own in the culture data.
            foreach ((string code, string name, string symbol, int digits) in new[]
            {
                ("EUR", "Euro", "€", 2), ("USD", "US Dollar", "$", 2), ("XAU", "Gold", "XAU", 0), ("XAG", "Silver", "XAG", 0),
                ("XDR", "Special Drawing Rights", "XDR", 0), ("XOF", "West African CFA Franc", "CFA", 0), ("XAF", "Central African CFA Franc", "FCFA", 0),
                ("XCD", "East Caribbean Dollar", "$", 2), ("XPF", "CFP Franc", "F", 0), ("BTN", "Bhutanese Ngultrum", "Nu.", 2),
            })
            {
                currencies.TryAdd(code, new CurrencyInfo { Name = name, Symbol = symbol, Digits = digits });
            }
            // Codes the culture data gets wrong or leaves out.
            currencies["JPY"].Digits = 0;
            return (regions, currencies);
        }

        /// <summary>The country for a two- or three-letter code or an English name (GB, GBR, United Kingdom), or null.</summary>
        public static RegionInfo Find(string text)
        {
            if (text == "UK" || text == "uk") text = "GB";
            return Data.Value.ByKey.TryGetValue(text.Trim(), out RegionInfo region) ? region : null;
        }

        public static string Closest(string text) =>
            Values.Closest(text, Data.Value.ByKey.Values.Select(r => r.TwoLetterISORegionName).Distinct()) is string code ? code
            : Values.Closest(text, Data.Value.ByKey.Values.Select(r => r.EnglishName).Distinct());

        public static CurrencyInfo Currency(string code) => Data.Value.Currencies.TryGetValue(code, out CurrencyInfo c) ? c : null;

        /// <summary>ITU country calling codes (E.164), as prefixes: at most one of them starts a number.</summary>
        private static readonly HashSet<string> CallingCodes = new HashSet<string>((
            "1 7 20 27 30 31 32 33 34 36 39 40 41 43 44 45 46 47 48 49 51 52 53 54 55 56 57 58 60 61 62 63 64 65 66 81 82 84 86 90 91 92 93 94 95 98 " +
            "211 212 213 216 218 220 221 222 223 224 225 226 227 228 229 230 231 232 233 234 235 236 237 238 239 240 241 242 243 244 245 246 247 248 249 " +
            "250 251 252 253 254 255 256 257 258 260 261 262 263 264 265 266 267 268 269 290 291 297 298 299 " +
            "350 351 352 353 354 355 356 357 358 359 370 371 372 373 374 375 376 377 378 379 380 381 382 383 385 386 387 389 " +
            "420 421 423 500 501 502 503 504 505 506 507 508 509 590 591 592 593 594 595 596 597 598 599 " +
            "670 672 673 674 675 676 677 678 679 680 681 682 683 685 686 687 688 689 690 691 692 " +
            "800 808 850 852 853 855 856 870 878 880 881 882 883 886 888 " +
            "960 961 962 963 964 965 966 967 968 970 971 972 973 974 975 976 977 979 992 993 994 995 996 998").Split(' '));

        /// <summary>The calling code that starts these digits (44 for 447700900123), or null.</summary>
        public static string CallingCode(string digits)
        {
            for (int length = 1; length <= 3 && length <= digits.Length; length++)
            {
                string prefix = digits.Substring(0, length);
                if (CallingCodes.Contains(prefix)) return prefix;
            }
            return null;
        }
    }
}
