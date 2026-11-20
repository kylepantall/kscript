using System.Text;

using KScript.Runtime;

namespace KScript.Documents.Text
{
    /// <summary>Reads BibTeX (.bib) files and formats citations and reference lists.</summary>
    public static class BibTex
    {
        /// <summary>The entries of a .bib file: @article{key, author = {...}, year = 1970}.</summary>
        public static List<Source> Parse(string text)
        {
            var sources = new List<Source>();
            int pos = 0;
            text ??= string.Empty;
            var macros = new Dictionary<string, string>(Months, StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                int at = text.IndexOf('@', pos);
                if (at < 0) break;
                pos = at + 1;
                int open = text.IndexOfAny(new[] { '{', '(' }, pos);
                if (open < 0) break;
                string type = text.Substring(pos, open - pos).Trim().ToLowerInvariant();
                if (type.Length == 0 || !type.All(char.IsLetter))
                {
                    continue;
                }
                char close = text[open] == '{' ? '}' : ')';
                int end = Matching(text, open, text[open], close);
                if (end < 0) throw new KScriptException($"The .bib entry @{type} that starts at character {at} isn't closed with {close}.", "Format");
                pos = end + 1;
                string body = text.Substring(open + 1, end - open - 1);
                if (type == "string")
                {
                    // @string{jacm = "Journal of the ACM"}: later entries can write journal = jacm.
                    var macro = new Source();
                    ReadFields(body, macro, macros);
                    foreach (var kv in macro.Fields) macros[kv.Key] = kv.Value;
                    continue;
                }
                if (type == "comment" || type == "preamble") continue;
                int comma = body.IndexOf(',');
                var source = new Source { Type = type, Key = (comma < 0 ? body : body.Substring(0, comma)).Trim() };
                if (comma >= 0) ReadFields(body.Substring(comma + 1), source, macros);
                sources.Add(source);
            }
            return sources;
        }

        private static int Matching(string text, int open, char o, char c)
        {
            int depth = 0;
            bool quoted = false;
            for (int i = open; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\\')
                {
                    i++;
                    continue;
                }
                if (ch == '"' && depth == 1 && o == '{') quoted = !quoted;
                if (quoted) continue;
                if (ch == o) depth++;
                else if (ch == c && --depth == 0) return i;
            }
            return -1;
        }

        private static string ProtectBraced(string value)
        {
            var sb = new StringBuilder(value.Length);
            int depth = 0;
            foreach (char c in value)
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
                sb.Append(c == ' ' && depth > 0 ? '\u00A0' : c);
            }
            return sb.ToString();
        }

        /// <summary>The month macros every BibTeX style knows: month = jan.</summary>
        private static readonly Dictionary<string, string> Months = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["jan"] = "January", ["feb"] = "February", ["mar"] = "March", ["apr"] = "April", ["may"] = "May", ["jun"] = "June",
            ["jul"] = "July", ["aug"] = "August", ["sep"] = "September", ["oct"] = "October", ["nov"] = "November", ["dec"] = "December",
        };

        private static void ReadFields(string body, Source source, Dictionary<string, string> macros)
        {
            int i = 0;
            while (i < body.Length)
            {
                int eq = body.IndexOf('=', i);
                if (eq < 0) break;
                string name = body.Substring(i, eq - i).Trim().Trim(',').Trim().ToLowerInvariant();
                int v = eq + 1;
                while (v < body.Length && char.IsWhiteSpace(body[v])) v++;
                string value;
                if (v < body.Length && (body[v] == '{' || body[v] == '"'))
                {
                    int end = body[v] == '{' ? Matching(body, v, '{', '}') : body.IndexOf('"', v + 1);
                    if (end < 0) end = body.Length - 1;
                    value = body.Substring(v + 1, end - v - 1);
                    i = end + 1;
                }
                else
                {
                    int end = body.IndexOf(',', v);
                    if (end < 0) end = body.Length;
                    value = body.Substring(v, end - v).Trim();
                    if (macros.TryGetValue(value, out string expanded)) value = expanded;
                    i = end;
                }
                // {World Health Organization} in an author list is one name: its spaces become non-breaking ones.
                if (name == "author" || name == "editor") value = ProtectBraced(value);
                if (name.Length > 0) source.Fields[name] = Clean(value);
                int next = body.IndexOf(',', i);
                if (next < 0) break;
                i = next + 1;
            }
        }

        /// <summary>TeX accent commands and the combining marks they put on the next letter: \"o is ö.</summary>
        private static readonly Dictionary<char, char> Accents = new Dictionary<char, char>
        {
            ['"'] = '\u0308', ['\''] = '\u0301', ['`'] = '\u0300', ['^'] = '\u0302', ['~'] = '\u0303', ['='] = '\u0304', ['.'] = '\u0307', ['c'] = '\u0327', ['v'] = '\u030C', ['u'] = '\u0306', ['H'] = '\u030B',
        };

        /// <summary>A BibTeX value as plain text: braces removed, accents, quotes, \&amp; and -- turned into characters, spaces tidied.</summary>
        internal static string Clean(string value)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '{' || c == '}') continue;
                if (c == '\\' && i + 2 < value.Length && Accents.TryGetValue(value[i + 1], out char mark) && (!char.IsLetter(value[i + 1]) || value[i + 2] == '{' || value[i + 2] == ' '))
                {
                    // An accent: \"o, \'{e}, \^{\i}.
                    int j = i + 2;
                    while (j < value.Length && (value[j] == '{' || value[j] == ' ')) j++;
                    if (j + 1 < value.Length && value[j] == '\\' && value[j + 1] == 'i') value = value.Remove(j, 1);
                    if (j < value.Length && char.IsLetter(value[j]))
                    {
                        sb.Append((value[j].ToString() + mark).Normalize(NormalizationForm.FormC));
                        i = j;
                        continue;
                    }
                }
                if (c == '`' || c == '\'')
                {
                    // TeX quotes: ``double'' and `single'.
                    bool twice = i + 1 < value.Length && value[i + 1] == c;
                    sb.Append(c == '`' ? (twice ? '\u201C' : '\u2018') : (twice ? '\u201D' : '\u2019'));
                    if (twice) i++;
                    continue;
                }
                if (c == '\\' && i + 1 < value.Length && !char.IsLetter(value[i + 1]))
                {
                    sb.Append(value[++i]);
                    continue;
                }
                if (c == '-' && i + 1 < value.Length && value[i + 1] == '-')
                {
                    sb.Append('–');
                    while (i + 1 < value.Length && value[i + 1] == '-') i++;
                    continue;
                }
                sb.Append(char.IsWhiteSpace(c) && c != '\u00A0' ? ' ' : c);
            }
            return string.Join(" ", sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>The authors of a source, each as (first names, last name).</summary>
        public static List<(string First, string Last)> Authors(Source source)
        {
            string authors = source["author"] ?? source["editor"];
            var list = new List<(string, string)>();
            if (authors == null) return list;
            foreach (string raw in authors.Split(new[] { " and " }, StringSplitOptions.RemoveEmptyEntries))
            {
                string a = raw.Trim();
                if (a.Length == 0) continue;
                int comma = a.IndexOf(',');
                if (comma >= 0)
                {
                    list.Add((a.Substring(comma + 1).Trim(), a.Substring(0, comma).Trim()));
                    continue;
                }
                int space = a.LastIndexOf(' ');
                list.Add(space < 0 ? (string.Empty, a) : (a.Substring(0, space).Trim(), a.Substring(space + 1).Trim()));
            }
            return list;
        }

        private static string Initials(string first) =>
            string.Join(" ", first.Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Contains('-') ? string.Join("-", p.Split('-').Where(x => x.Length > 0).Select(x => x[0] + ".")) : p[0] + "."));

        /// <summary>The label of an author-year citation: Conway, 1970; Conway and Gardner, 1970; Conway et al., 1970.</summary>
        public static string AuthorYear(Source source)
        {
            var authors = Authors(source);
            string names = authors.Count switch
            {
                0 => source["title"] ?? source.Key,
                1 => authors[0].Last,
                2 => authors[0].Last + " and " + authors[1].Last,
                _ => authors[0].Last + " et al.",
            };
            return names + ", " + (source["year"] ?? "n.d.");
        }

        /// <summary>The entry in a reference list, as inlines so titles can be italic and links clickable.</summary>
        public static List<Inline> Format(Source source, CitationStyle style)
        {
            var parts = new List<Inline>();
            var authors = Authors(source);
            string names;
            if (style == CitationStyle.AuthorYear)
            {
                names = Join(authors.Select((a, i) => i == 0 ? (a.First.Length > 0 ? $"{a.Last}, {Initials(a.First)}" : a.Last) : (a.First.Length > 0 ? $"{Initials(a.First)} {a.Last}" : a.Last)).ToList());
            }
            else
            {
                names = Join(authors.Select(a => a.First.Length > 0 ? $"{Initials(a.First)} {a.Last}" : a.Last).ToList());
            }
            if (source["author"] == null && source["editor"] != null) names += authors.Count > 1 ? ", editors" : ", editor";
            string year = source["year"];
            if (names.Length > 0)
            {
                parts.Add(new TextInline(names + (style == CitationStyle.AuthorYear ? $" ({year ?? "n.d."}). " : ". ")));
            }
            else if (style == CitationStyle.AuthorYear)
            {
                parts.Add(new TextInline($"({year ?? "n.d."}). "));
            }

            string title = source["title"];
            string container = source["journal"] ?? source["booktitle"];
            bool italicTitle = container == null;
            if (title != null)
            {
                if (italicTitle)
                {
                    var em = new EmphasisInline();
                    em.Children.Add(new TextInline(title));
                    parts.Add(em);
                    parts.Add(new TextInline(". "));
                }
                else
                {
                    parts.Add(new TextInline(title + ". "));
                }
            }

            var details = new List<string>();
            if (container != null)
            {
                var em = new EmphasisInline();
                em.Children.Add(new TextInline((source["booktitle"] != null && source["journal"] == null ? "In " : string.Empty) + container));
                parts.Add(em);
                string volume = source["volume"];
                string number = source["number"];
                string pages = source["pages"];
                var sb = new StringBuilder();
                if (volume != null) sb.Append(", ").Append(volume);
                if (number != null) sb.Append('(').Append(number).Append(')');
                if (pages != null) sb.Append(volume != null ? ":" : ", pp. ").Append(pages);
                parts.Add(new TextInline(sb.ToString()));
            }
            foreach (string field in new[] { "publisher", "institution", "school", "organization", "address", "edition", "howpublished" })
            {
                if (source[field] != null) details.Add(field == "edition" ? source[field] + " edition" : source[field]);
            }
            if (style != CitationStyle.AuthorYear && year != null) details.Add(year);
            if (details.Count > 0) parts.Add(new TextInline((container != null ? ", " : string.Empty) + string.Join(", ", details) + "."));
            else if (container != null) parts.Add(new TextInline("."));

            string link = source["doi"] != null ? "https://doi.org/" + source["doi"].Replace("https://doi.org/", string.Empty) : source["url"];
            if (link != null)
            {
                parts.Add(new TextInline(" "));
                var l = new LinkInline { Url = link };
                l.Children.Add(new TextInline(source["doi"] != null ? "doi:" + source["doi"].Replace("https://doi.org/", string.Empty) : link));
                parts.Add(l);
            }
            if (source["note"] != null) parts.Add(new TextInline(" " + source["note"] + "."));
            // An entry with nothing to show is listed by its key, so the citation still leads somewhere.
            if (parts.Count == 0) parts.Add(new TextInline(source.Key + "."));
            return parts;
        }

        private static string Join(List<string> names) => names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            2 => names[0] + " and " + names[1],
            _ => string.Join(", ", names.Take(names.Count - 1)) + ", and " + names[^1],
        };
    }
}
