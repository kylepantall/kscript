using KScript.Documents.Text;
using KScript.Runtime;

namespace KScript.Documents
{
    /// <summary>What a document's numbers and references came to: the writers all read this, so they agree.</summary>
    public sealed class Resolved
    {
        /// <summary>Every section, in order, for the contents and PDF bookmarks.</summary>
        public List<Section> Sections { get; } = new List<Section>();

        /// <summary>The sources cited, in the order the reference list shows them.</summary>
        public List<Source> Cited { get; } = new List<Source>();

        /// <summary>What [#id] says for each id: "2.1", "3", "(4)".</summary>
        public Dictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The block each id names.</summary>
        public Dictionary<string, Block> Targets { get; } = new Dictionary<string, Block>(StringComparer.Ordinal);

        public List<FootnoteInline> Footnotes { get; } = new List<FootnoteInline>();

        public bool HasBibliography { get; set; }
    }

    /// <summary>
    /// Numbers sections, tables, figures, equations and footnotes, and fills in cross-references and citations. Every
    /// writer runs it first, so a PDF and a Word file of the same document say the same things.
    /// </summary>
    public static class Resolver
    {
        public static Resolved Resolve(Document doc)
        {
            var r = new Resolved();
            int tables = 0, figures = 0, equations = 0;
            var counters = new int[8];

            void Number(IEnumerable<Block> blocks, int level)
            {
                foreach (Block b in blocks)
                {
                    switch (b)
                    {
                        case Section s:
                        {
                            s.Level = Math.Min(level, 6);
                            s.NewPage = s.NewPage || (doc.Style.ChapterPages && s.Level == 1);
                            if (s.Numbered && doc.Style.NumberSections)
                            {
                                counters[s.Level]++;
                                for (int i = s.Level + 1; i < counters.Length; i++) counters[i] = 0;
                                s.Number = string.Join(".", counters.Skip(1).Take(s.Level).Select(n => Math.Max(n, 1)));
                            }
                            else
                            {
                                s.Number = null;
                            }
                            r.Sections.Add(s);
                            Label(r, s, s.Number ?? InlineParser.Plain(s.Title));
                            Number(s.Blocks, level + 1);
                            continue;
                        }
                        case Table t:
                            t.Number = t.Caption != null ? ++tables : 0;
                            Label(r, t, t.Number.ToString());
                            break;
                        case Figure f:
                            f.Number = f.Caption != null ? ++figures : 0;
                            Label(r, f, f.Number.ToString());
                            break;
                        case MathBlock m:
                            m.Number = m.Numbered ? ++equations : 0;
                            Label(r, m, $"({m.Number})");
                            break;
                        case Bibliography:
                            r.HasBibliography = true;
                            break;
                        default:
                            Label(r, b, null);
                            break;
                    }
                    Number(b.Children(), level);
                }
            }

            Number(doc.Abstract, 1);
            Number(doc.Blocks, 1);

            // Citations and references, in reading order.
            var order = new List<Source>();
            int footnotes = 0;
            void Inlines(IEnumerable<Inline> inlines)
            {
                foreach (Inline i in inlines ?? Enumerable.Empty<Inline>())
                {
                    switch (i)
                    {
                        case RefInline re:
                            if (!r.Labels.TryGetValue(re.Id, out string label))
                                throw new KScriptException($"[#{re.Id}] refers to nothing: no section, table, figure or equation has id: {re.Id}.", "Reference");
                            re.Text = label;
                            break;
                        case CiteInline c:
                            foreach (string key in c.Keys)
                            {
                                if (!doc.Sources.TryGetValue(key, out Source s))
                                    throw new KScriptException($"[@{key}] cites a source the document doesn't have. Add it to the bibliography: Bibliography(path: \"refs.bib\") or Source(id: \"{key}\", ...).", "Reference");
                                if (!order.Contains(s)) order.Add(s);
                            }
                            break;
                        case StrongInline s:
                            Inlines(s.Children);
                            break;
                        case EmphasisInline e:
                            Inlines(e.Children);
                            break;
                        case LinkInline l:
                            Inlines(l.Children);
                            break;
                        case FootnoteInline f:
                            f.Number = ++footnotes;
                            r.Footnotes.Add(f);
                            Inlines(f.Children);
                            break;
                    }
                }
            }

            foreach (Block b in doc.AllBlocks()) Inlines(InlinesOf(b));

            List<Source> cited = doc.Style.Citations == CitationStyle.AuthorYear
                ? order.OrderBy(s => BibTex.Authors(s).FirstOrDefault().Last ?? s["title"] ?? s.Key, StringComparer.OrdinalIgnoreCase).ThenBy(s => s["year"]).ToList()
                : order;
            for (int i = 0; i < cited.Count; i++) cited[i].Number = i + 1;
            r.Cited.AddRange(cited);

            // Now every source has its number, the citations can say it.
            foreach (Block b in doc.AllBlocks()) Cites(InlinesOf(b), doc);
            if (r.Cited.Count > 0 && !r.HasBibliography)
            {
                doc.Blocks.Add(new Bibliography());
                r.HasBibliography = true;
            }
            return r;
        }

        private static void Cites(IEnumerable<Inline> inlines, Document doc)
        {
            foreach (Inline i in inlines ?? Enumerable.Empty<Inline>())
            {
                switch (i)
                {
                    case CiteInline c:
                        c.Text = doc.Style.Citations == CitationStyle.AuthorYear
                            ? "(" + string.Join("; ", c.Keys.Select(k => BibTex.AuthorYear(doc.Sources[k]))) + ")"
                            : "[" + string.Join(", ", c.Keys.Select(k => doc.Sources[k].Number)) + "]";
                        break;
                    case StrongInline s:
                        Cites(s.Children, doc);
                        break;
                    case EmphasisInline e:
                        Cites(e.Children, doc);
                        break;
                    case LinkInline l:
                        Cites(l.Children, doc);
                        break;
                    case FootnoteInline f:
                        Cites(f.Children, doc);
                        break;
                }
            }
        }

        private static void Label(Resolved r, Block b, string label)
        {
            if (b.Id == null) return;
            if (r.Targets.ContainsKey(b.Id)) throw new KScriptException($"Two parts of the document have id: {b.Id}. Each id names one section, table, figure or equation.", "Reference");
            r.Targets[b.Id] = b;
            r.Labels[b.Id] = label ?? b.Id;
        }

        /// <summary>The inline text a block holds directly (not its child blocks').</summary>
        public static IEnumerable<Inline> InlinesOf(Block b) => b switch
        {
            Section s => s.Title,
            Paragraph p => p.Inlines,
            Table t => (t.Caption ?? new List<Inline>()).Concat(t.Rows.SelectMany(row => row.Cells).SelectMany(c => c.Inlines)),
            Figure f => f.Caption ?? Enumerable.Empty<Inline>(),
            _ => Enumerable.Empty<Inline>(),
        };
    }
}
