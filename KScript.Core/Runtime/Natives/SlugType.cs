using System.Globalization;
using System.Text;

using KScript.Metadata;

namespace KScript.Runtime
{
    /// <summary>
    /// <c>slug</c>: lowercase words joined by -, for URLs (my-first-post). Slugs sort alphabetically, and @slugify
    /// makes one from any text.
    /// </summary>
    public sealed class SlugType : NativeType
    {
        public override string Name => "slug";

        public override string Description => "Lowercase words joined by -, for URLs: my-first-post.";

        public override string Example => "my-first-post";

        public override RawText RawText => RawText.Word;

        public override IReadOnlyList<string> StringMethods { get; } = NativeTypes.Searchable.Concat(new[] { "split", "upper", "replace" }).ToArray();

        public override IReadOnlyList<string> Completions => Array.Empty<string>();

        public override Equality Equality => Equality.ByText;

        // Slugs are lowercase ASCII, so alphabetical order is the natural order for a list of posts or pages.
        public override Ordering Ordering => Ordering.ByText;

        public override Iteration Iteration => Iteration.None;

        protected override void DeclareOperators(NativeOperators o)
        {
        }

        protected override void DeclareListMembers(NativeListMembers l)
        {
        }

        protected override void DeclareMembers(NativeMembers m)
        {
            m.Field("words", KType.List, "The words: ['my', 'first', 'post'].", (t, _) => new List<object>((List<object>)((NativeText)t).Part("words")),
                "<let name=\"x\" type=\"slug\" value=\"'my-first-post'\" />\n<echo>{ $x.words }</echo>");
            m.Package.Command("slugify", "text: string", "Turns text into a slug: 'Hello, World!' gives hello-world. Accents are dropped.", "slug",
                c => Parse(Slugify(c.Str(0))).Value, "@slugify('Crème Brûlée recipes!')");
        }

        public override (object Value, string Reason) Parse(string s)
        {
            s = s.Trim();
            if (s.Length == 0) return (null, "it is empty");
            if (s.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')))
                return (null, $"use lowercase letters, digits and - (such as {Slugify(s)})");
            if (s[0] == '-' || s[^1] == '-') return (null, "it can't start or end with -");
            if (s.Contains("--")) return (null, "it can't have two dashes in a row");
            return (new NativeText("slug", s, new Dictionary<string, object> { ["words"] = s.Split('-').Cast<object>().ToList() }), null);
        }

        /// <summary>Text made into a slug: 'Hello, World!' becomes hello-world. Always a valid slug.</summary>
        internal static string Slugify(string s)
        {
            string plain = new string(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            var sb = new StringBuilder();
            foreach (char c in plain.ToLowerInvariant())
            {
                if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
            }
            string slug = sb.ToString().Trim('-');
            return slug.Length == 0 ? "post" : slug;
        }
    }
}
