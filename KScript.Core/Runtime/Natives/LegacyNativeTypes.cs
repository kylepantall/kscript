using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// TEMPORARY: the native types not yet moved onto <see cref="NativeType"/>. Each moves to a class of its own in
    /// this folder, listed in <see cref="NativeTypeRegistry.BuiltIn"/>; when the last one has moved, this file goes.
    /// </summary>
    internal static class LegacyNativeTypes
    {
        private static readonly string[] Searchable = NativeTypes.Searchable;

        private static string[] With(params string[] more) => Searchable.Concat(more).ToArray();

        private static void Add(NativeTypeInfo info) => NativeTypes.Register(info);

        public static void Register()
        {
            // People and places.
            Add(new NativeTypeInfo("phone", "A phone number in E.164: + then the country code and number, 8 to 15 digits. Spaces, dots, dashes and brackets are dropped.", "+447700900123",
                Searchable) { Options = new[] { "countries" } });
            Add(new NativeTypeInfo("country", "An ISO 3166 country code. Two-letter, three-letter and English names are read; it is kept as two capitals, such as GB.", "GB",
                Searchable));
            Add(new NativeTypeInfo("currency", "An ISO 4217 currency code, kept in capitals: GBP, USD, EUR.", "GBP",
                Searchable));
            Add(new NativeTypeInfo("locale", "A language tag (BCP 47) such as en, en-GB or pt-BR, in its usual case.", "en-GB",
                With("split")));

            // Money.
            Add(new NativeTypeInfo("iban", "An international bank account number, checked with its mod-97 digits. Kept in capitals without spaces.", "GB82WEST12345698765432",
                Searchable) { Article = "an" });
            Add(new NativeTypeInfo("card", "A payment card number, checked with the Luhn digit. It prints masked apart from the last four digits.", "'4242 4242 4242 4242'",
                Array.Empty<string>()) { Secret = true });
        }
    }
}
