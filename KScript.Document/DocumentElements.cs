using System.Globalization;

using KScript.Documents.Maths;
using KScript.Documents.Text;
using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Documents
{
    /// <summary>
    /// Runs the KScript.Document elements. A document block opens a <see cref="Builder"/>; the elements inside add
    /// blocks to whatever is open (the document, a section, a list item), so for, if and fn work between them.
    /// </summary>
    internal static class DocumentElements
    {
        /// <summary>The document being built on this thread, and where new blocks go.</summary>
        private sealed class Builder
        {
            public Document Doc;
            public readonly Stack<List<Block>> Targets = new Stack<List<Block>>();
            public readonly Stack<ListBlock> Lists = new Stack<ListBlock>();
            public Table Table;
            public Bibliography Bibliography;

            /// <summary>How many sections are open: a section inside five others is the deepest there is.</summary>
            public int SectionDepth;

            public List<Block> Target => Targets.Peek();
        }

        [ThreadStatic]
        private static Builder current;

        /// <summary>
        /// A document draws the components written in it. A component's render adds blocks with the elements it
        /// holds, or returns Markdown or LaTeX that is read into blocks here.
        /// </summary>
        internal sealed class Surface : RenderSurface
        {
            public override string Name => KScript.Syntax.Markup.ComponentScript.Document;

            public override bool Active => current != null;

            // A document is drawn once, so a component in it has nothing to keep.
            public override Record Instance(ElementRun run, object id, Func<Record, Record> make) => make(null);

            public override void Returned(ElementRun run, object value)
            {
                if (value is not ComponentSource source) return;
                current.Target.AddRange(Located(run, () =>
                {
                    string text = source.Fill(run.Interpreter, Export.LatexText.Escape);
                    return source.Latex ? LatexBlocks.Parse(text) : MarkdownBlocks.Parse(text, dollarMaths: false);
                }));
            }
        }

        private static Builder Current(ElementRun run) =>
            current ?? throw run.Error($"{run.Element.Name} only works inside a Document block.", null, "Syntax");

        private static void Into(ElementRun run, List<Block> target, Action body)
        {
            Builder b = Current(run);
            b.Targets.Push(target);
            try
            {
                body();
            }
            finally
            {
                b.Targets.Pop();
            }
        }

        /// <summary>Runs <paramref name="make"/>, pointing errors (bad maths, a missing file) at the element.</summary>
        private static T Located<T>(ElementRun run, Func<T> make, string attribute = null)
        {
            try
            {
                return make();
            }
            catch (KScriptException e) when (e.Line == 0)
            {
                throw run.Error(e.Words, attribute, e.Type);
            }
        }

        private static List<Inline> Inlines(ElementRun run, string text, bool keepLines = false) => Located(run, () => InlineParser.Parse(text, keepLines));

        /// <summary>Text split into paragraphs at blank lines.</summary>
        private static IEnumerable<Paragraph> Paragraphs(ElementRun run, string text)
        {
            foreach (string part in System.Text.RegularExpressions.Regex.Split(text.Replace("\r\n", "\n"), @"\n[ \t]*\n"))
            {
                if (part.Trim().Length == 0) continue;
                yield return new Paragraph { Inlines = Inlines(run, part.Trim()) };
            }
        }

        // ---- the document ----------------------------------------------------------------------------------------

        internal static bool? Document(ElementRun run)
        {
            if (current != null) throw run.Error("A Document can't be inside another one.", null, "Syntax");
            var doc = new Document { BaseDirectory = run.Module.Directory ?? run.Host.WorkingDirectory };
            Located(run, () => doc.Style = Templates.Get(run.Template("template", "plain")), "template");
            foreach (string option in DocumentStyle.Options)
            {
                if (!run.Has(option)) continue;
                try
                {
                    doc.Style.Set(option, run.Template(option));
                }
                catch (KScriptException e) when (e.Line == 0)
                {
                    // Most settings name themselves in their message (paper: 'a9' isn't a paper size).
                    throw run.Error(e.Message.StartsWith(option, StringComparison.Ordinal) ? e.Message : $"{option}: {e.Message}", option, e.Type);
                }
            }
            doc.Title = Blank(run.Template("title"));
            doc.Subtitle = Blank(run.Template("subtitle"));
            doc.Date = Blank(run.Template("date"));
            foreach (string name in (run.Template("author") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                doc.Authors.Add(new Author { Name = name });
            doc.Keywords.AddRange(KScript.Metadata.NameList.Names(run.Template("keywords")));

            string into = run.Has("into") ? run.VariableName("into") : null;
            string to = run.Template("to");
            if (into == null && string.IsNullOrWhiteSpace(to))
                throw run.Error("Document needs to: (where to save it, such as to: \"paper.pdf\") or into: (a variable to keep it in).", null, "Syntax");

            var builder = new Builder { Doc = doc };
            builder.Targets.Push(doc.Blocks);
            current = builder;
            try
            {
                run.RunChildren();
            }
            finally
            {
                current = null;
            }

            var value = new DocumentValue(doc);
            if (!string.IsNullOrWhiteSpace(to))
            {
                foreach (string path in OutputPaths(to))
                    Located(run, () => { value.Save(run.Host, path, run.Module.Directory); return true; }, "to");
            }
            if (into != null) run.Scope.Declare(into, value);
            return null;
        }

        /// <summary>
        /// The files to: names. A list, <c>[paper.pdf, "paper.docx"]</c>, or comma separated text, <c>"paper.pdf, paper.docx"</c>.
        /// Quotes around an entry are dropped.
        /// </summary>
        internal static List<string> OutputPaths(string to) =>
            NameList.Names(to).Select(Unquote).Where(p => p.Length > 0).ToList();

        private static string Unquote(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0]) s = s.Substring(1, s.Length - 2).Trim();
            return s;
        }

        private static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        internal static bool? Author(ElementRun run)
        {
            Builder b = Current(run);
            b.Doc.Authors.Add(new Author { Name = run.Text(), Affiliation = Blank(run.Template("affiliation")), Email = Blank(run.Template("email")) });
            return null;
        }

        internal static bool? Abstract(ElementRun run)
        {
            Current(run).Doc.Abstract.AddRange(Paragraphs(run, run.Text()));
            return null;
        }

        // ---- blocks ------------------------------------------------------------------------------------------------

        /// <summary>Headings have six levels in every format (h1 to h6, Word's Heading 1 to 6).</summary>
        internal const int MaxSectionDepth = 6;

        internal static bool? Section(ElementRun run)
        {
            Builder b = Current(run);
            if (b.SectionDepth >= MaxSectionDepth)
                throw run.Error($"Sections go {MaxSectionDepth} levels deep at most, and this one is inside {b.SectionDepth} others. Put it next to the section it is in.", null, "Syntax");
            var section = new Section
            {
                Title = Inlines(run, run.RequiredTemplate("title")),
                Id = Blank(run.Template("id")),
                Numbered = run.Flag("numbered", true),
                NewPage = run.Flag("new_page"),
            };
            b.Target.Add(section);
            b.SectionDepth++;
            try
            {
                Into(run, section.Blocks, () => run.RunChildren());
            }
            finally
            {
                b.SectionDepth--;
            }
            return null;
        }

        internal static bool? Paragraph(ElementRun run)
        {
            Builder b = Current(run);
            string text = run.Text();
            bool lines = run.Flag("lines");
            PaperAlign? align = run.Has("align") ? Located(run, () => Units.Align(run.Template("align")), "align") : null;
            var paragraphs = lines ? new List<Paragraph> { new Paragraph { Inlines = Inlines(run, text, keepLines: true), KeepLines = true, NoIndent = true } } : Paragraphs(run, text).ToList();
            for (int i = 0; i < paragraphs.Count; i++)
            {
                paragraphs[i].Align = align;
                if (i == 0) paragraphs[i].Id = Blank(run.Template("id"));
                b.Target.Add(paragraphs[i]);
            }
            return null;
        }

        internal static bool? Markdown(ElementRun run)
        {
            Builder b = Current(run);
            List<Block> blocks;
            if (run.Has("path"))
            {
                string path = Located(run, () => run.Host.ResolvePath(run.Template("path"), run.Module.Directory), "path");
                if (!File.Exists(path)) throw run.Error($"There is no Markdown file at {path}.", "path", "IO");
                blocks = Located(run, () => MarkdownBlocks.Parse(File.ReadAllText(path), dollarMaths: true), "path");
            }
            else
            {
                blocks = Located(run, () => MarkdownBlocks.Parse(run.Text(), dollarMaths: false));
            }
            b.Target.AddRange(blocks);
            return null;
        }

        internal static bool? List(ElementRun run)
        {
            Builder b = Current(run);
            var list = new ListBlock
            {
                Style = (run.Template("style", "bullet")).ToLowerInvariant() switch
                {
                    "number" or "numbered" or "decimal" => ListStyle.Number,
                    "letter" or "alpha" => ListStyle.Letter,
                    "roman" => ListStyle.Roman,
                    "bullet" => ListStyle.Bullet,
                    string other => throw run.Error($"style: '{other}' should be bullet, number, letter or roman.", "style", "Argument"),
                },
                Start = run.Has("start") ? Values.ToInt(run.Eval("start"), "List start") : 1,
            };
            if (b.Lists.Count > 0 && b.Targets.Peek() == null)
            {
                // A List straight inside a List belongs to the item before it.
                ListBlock outer = b.Lists.Peek();
                if (outer.Items.Count == 0) throw run.Error("A List inside a List goes after the Item it belongs to.", null, "Syntax");
                outer.Items[^1].Blocks.Add(list);
            }
            else
            {
                b.Target.Add(list);
            }
            b.Lists.Push(list);
            b.Targets.Push(null);
            try
            {
                run.RunChildren();
            }
            finally
            {
                b.Targets.Pop();
                b.Lists.Pop();
            }
            return null;
        }

        internal static bool? Item(ElementRun run)
        {
            Builder b = Current(run);
            // Item must be inside a List (the checker sees to that), so a list is open.
            ListBlock list = b.Lists.Peek();
            var item = new ListItem();
            item.Blocks.AddRange(Paragraphs(run, run.Text()));
            list.Items.Add(item);
            return null;
        }

        internal static bool? Table(ElementRun run)
        {
            Builder b = Current(run);
            var table = new Table
            {
                Id = Blank(run.Template("id")),
                Caption = run.Has("caption") ? Inlines(run, run.Template("caption")) : null,
                Rules = run.Has("rules") ? Located(run, () => Units.Rules(run.Template("rules")), "rules") : b.Doc.Style.TableRules,
                Scale = run.Number("size", 1),
            };
            if (run.Has("widths"))
            {
                foreach (object w in run.EvalList("widths").SelectMany(x => x is List<object> l ? l : new List<object> { x }))
                    table.Widths.Add(Values.ToNumber(w, "Table widths"));
            }
            if (run.Has("rows")) FromRows(run, table);
            else if (run.Has("align")) table.Align.AddRange(AlignWords(TableArguments.Read(new List<object>(), null, run.Eval("align"), false, (m, a) => run.Error(m, a, "Argument")).Align));
            b.Target.Add(table);
            Table outer = b.Table;
            b.Table = table;
            try
            {
                run.RunChildren();
            }
            finally
            {
                b.Table = outer;
            }
            return null;
        }

        /// <summary>Fills a table from rows:, columns:, align: and header:, read the way the terminal's table reads them.</summary>
        private static void FromRows(ElementRun run, Table table)
        {
            TableArguments given = TableArguments.Read(run.Eval("rows"), run.Has("columns") ? run.Eval("columns") : null, run.Has("align") ? run.Eval("align") : null,
                run.Flag("header", true), (message, argument) => run.Error(message, argument, "Argument"));
            if (given.Headings != null)
            {
                var head = new TableRow { Header = true };
                foreach (string heading in given.Headings) head.Cells.Add(new TableCell { Inlines = new List<Inline> { new TextInline(heading) } });
                table.Rows.Add(head);
            }
            foreach (List<object> cells in given.Cells)
            {
                var row = new TableRow();
                foreach (object cell in cells) row.Cells.Add(new TableCell { Inlines = InlineParser.Parse(Format(cell)) });
                table.Rows.Add(row);
            }
            table.Align.AddRange(AlignWords(Enumerable.Range(0, given.ColumnCount).Select(given.AlignOf)));
        }

        private static IEnumerable<ColumnAlign> AlignWords(IEnumerable<string> words) =>
            words.Select(w => w switch { "right" => ColumnAlign.Right, "center" => ColumnAlign.Center, _ => ColumnAlign.Left });

        private static string Format(object value) => value switch
        {
            null => string.Empty,
            double d => d.ToString(System.Math.Abs(d - System.Math.Round(d)) < 1e-9 ? "#,0" : "#,0.##", CultureInfo.InvariantCulture),
            _ => Values.ToStr(value),
        };

        internal static bool? Row(ElementRun run)
        {
            Builder b = Current(run);
            Table table = b.Table;
            string text = run.Text();
            table.Rows.Add(Located(run, () => MarkdownBlocks.Row(text, run.Flag("header"), s => InlineParser.Parse(s))));
            return null;
        }

        internal static bool? Figure(ElementRun run)
        {
            Builder b = Current(run);
            double width = 0;
            if (run.Has("width"))
            {
                object w = run.Eval("width");
                string text = Values.ToStr(w).Trim();
                width = text.EndsWith("%", StringComparison.Ordinal) ? Values.ToNumber(text.TrimEnd('%'), "Figure width") / 100 : Values.ToNumber(w, "Figure width");
                if (width <= 0 || width > 1) throw run.Error($"width: {text} should be a share of the text width from 0 to 1, such as 0.6 or '60%'.", "width", "Argument");
            }
            string src = run.RequiredTemplate("path");
            string full = Located(run, () => run.Host.ResolvePath(src, run.Module.Directory), "path");
            var figure = new Figure
            {
                Source = System.IO.Path.GetRelativePath(b.Doc.BaseDirectory, full),
                Caption = run.Has("caption") ? Inlines(run, run.Template("caption")) : null,
                Id = Blank(run.Template("id")),
                Width = width,
                Image = Located(run, () => ImageData.Load(full), "path"),
            };
            b.Target.Add(figure);
            return null;
        }

        internal static bool? Math(ElementRun run)
        {
            Builder b = Current(run);
            string latex = run.Text();
            b.Target.Add(new MathBlock { Latex = latex, Node = Located(run, () => MathParser.Parse(latex)), Id = Blank(run.Template("id")), Numbered = run.Flag("numbered", true) });
            return null;
        }

        internal static bool? Code(ElementRun run)
        {
            Current(run).Target.Add(new CodeBlock { Text = run.Text(), Language = Blank(run.Template("language")), Id = Blank(run.Template("id")) });
            return null;
        }

        internal static bool? Quote(ElementRun run)
        {
            var quote = new Quote { Attribution = Blank(run.Template("by")) };
            quote.Blocks.AddRange(Paragraphs(run, run.Text()));
            Current(run).Target.Add(quote);
            return null;
        }

        internal static bool? Contents(ElementRun run)
        {
            Current(run).Target.Add(new Contents
            {
                Title = run.Template("title", "Contents"),
                Depth = run.Has("depth") ? System.Math.Max(1, Values.ToInt(run.Eval("depth"), "Contents depth")) : 3,
            });
            return null;
        }

        internal static bool? Bibliography(ElementRun run)
        {
            Builder b = Current(run);
            var bib = new Bibliography { Title = run.Template("title", "References") };
            if (run.Has("path"))
            {
                string path = Located(run, () => run.Host.ResolvePath(run.Template("path"), run.Module.Directory), "path");
                if (!File.Exists(path)) throw run.Error($"There is no BibTeX file at {path}.", "path", "IO");
                foreach (Source s in Located(run, () => BibTex.Parse(File.ReadAllText(path)), "path")) b.Doc.Sources[s.Key] = s;
            }
            b.Target.Add(bib);
            b.Bibliography = bib;
            run.RunChildren();
            return null;
        }

        internal static bool? Source(ElementRun run)
        {
            Builder b = Current(run);
            var source = new Source { Key = run.RequiredTemplate("id").Trim(), Type = Blank(run.Raw("type")) ?? "misc" };
            foreach (var attr in run.Element.Attributes)
            {
                if (attr.Name == "id" || attr.Name == "type") continue;
                source.Fields[attr.Name] = run.Template(attr.Name);
            }
            b.Doc.Sources[source.Key] = source;
            return null;
        }

        internal static bool? PageBreak(ElementRun run)
        {
            Current(run).Target.Add(new PageBreak());
            return null;
        }

        internal static bool? VSpace(ElementRun run)
        {
            Builder b = Current(run);
            b.Target.Add(new Spacer { Height = Located(run, () => Units.Length(run.RequiredTemplate("height")), "height") });
            return null;
        }

        internal static bool? Rule(ElementRun run)
        {
            Current(run).Target.Add(new Rule());
            return null;
        }

        // ---- letters and invoices ------------------------------------------------------------------------------

        internal static string Today() => DateTime.Today.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

        private static Paragraph Lines(string text, PaperAlign align, double spaceBefore = 0, bool bold = false) =>
            new Paragraph { Inlines = InlineParser.Parse(text.Replace("\\n", "\n"), keepLines: true), Align = align, KeepLines = true, NoIndent = true, SpaceBefore = spaceBefore, Bold = bold };

        internal static bool? Letter(ElementRun run)
        {
            Builder b = Current(run);
            List<Block> target = b.Target;
            string from = Blank(run.Template("from"));
            if (from != null) target.Add(Lines(from, PaperAlign.Right));
            target.Add(Lines(Blank(run.Template("date")) ?? Today(), PaperAlign.Right, spaceBefore: from != null ? 6 : 0));
            string to = Blank(run.Template("to"));
            if (to != null) target.Add(Lines(to, PaperAlign.Left, spaceBefore: 12));
            string subject = Blank(run.Template("subject"));
            if (subject != null) target.Add(Lines(subject, PaperAlign.Left, spaceBefore: 12, bold: true));
            string opening = Blank(run.Template("opening"));
            if (opening != null) target.Add(Lines(opening, PaperAlign.Left, spaceBefore: 6));
            run.RunChildren();
            string closing = Blank(run.Template("closing"));
            if (closing != null) target.Add(Lines(closing, PaperAlign.Left, spaceBefore: 6));
            string signature = Blank(run.Template("signature"));
            if (signature != null) target.Add(Lines(signature, PaperAlign.Left, spaceBefore: 36));
            return null;
        }

        private static readonly Dictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GBP"] = "£", ["USD"] = "$", ["EUR"] = "€", ["JPY"] = "¥", ["CNY"] = "¥", ["INR"] = "₹",
            ["AUD"] = "A$", ["CAD"] = "C$", ["NZD"] = "NZ$", ["CHF"] = "CHF ", ["SEK"] = "kr ", ["NOK"] = "kr ", ["DKK"] = "kr ",
        };

        internal static string Money(double amount, string currency)
        {
            string symbol = Symbols.TryGetValue(currency, out string s) ? s : currency.Length == 3 && currency.All(char.IsLetter) ? currency.ToUpperInvariant() + " " : currency;
            bool whole = currency.Equals("JPY", StringComparison.OrdinalIgnoreCase);
            string number = System.Math.Abs(amount).ToString(whole ? "#,0" : "#,0.00", CultureInfo.InvariantCulture);
            return (amount < 0 ? "−" : string.Empty) + symbol + number;
        }

        internal static bool? Invoice(ElementRun run)
        {
            Builder b = Current(run);
            List<Block> target = b.Target;
            string currency = Blank(run.Template("currency")) ?? "GBP";
            object itemsValue = run.Eval("items");
            if (!(itemsValue is List<object> items)) throw run.Error("items: should be a list of maps such as { description: 'Consulting', quantity: 3, price: 450 }.", "items", "Argument");

            target.Add(new Paragraph { Inlines = new List<Inline> { new TextInline(Blank(run.Template("title")) ?? "Invoice") }, Bold = true, Scale = 2, NoIndent = true, Align = PaperAlign.Left, Color = b.Doc.Style.Accent });

            // From on the left; number and dates on the right.
            var details = new List<string>();
            string number = Blank(run.Template("number"));
            if (number != null) details.Add($"**Invoice number:** {number}");
            details.Add($"**Date:** {Blank(run.Template("date")) ?? Today()}");
            string due = Blank(run.Template("due"));
            if (due != null) details.Add($"**Due:** {due}");
            var header = new Table { Rules = TableRules.None };
            header.Widths.AddRange(new[] { 1.0, 1.0 });
            header.Align.AddRange(new[] { ColumnAlign.Left, ColumnAlign.Right });
            var top = new TableRow();
            top.Cells.Add(new TableCell { Inlines = InlineParser.Parse((Blank(run.Template("from")) ?? string.Empty).Replace("\\n", "\n"), keepLines: true) });
            top.Cells.Add(new TableCell { Inlines = InlineParser.Parse(string.Join("\n", details), keepLines: true) });
            header.Rows.Add(top);
            target.Add(header);

            string to = Blank(run.Template("to"));
            if (to != null)
            {
                target.Add(Lines("**Bill to**", PaperAlign.Left, spaceBefore: 6));
                target.Add(Lines(to, PaperAlign.Left));
            }

            var table = new Table { Rules = TableRules.Booktabs };
            table.Widths.AddRange(new[] { 5.0, 1.2, 2.0, 2.0 });
            table.Align.AddRange(new[] { ColumnAlign.Left, ColumnAlign.Right, ColumnAlign.Right, ColumnAlign.Right });
            var head = new TableRow { Header = true };
            foreach (string h in new[] { "Description", "Qty", "Unit price", "Amount" }) head.Cells.Add(new TableCell { Inlines = new List<Inline> { new TextInline(h) } });
            table.Rows.Add(head);
            double subtotal = 0;
            foreach (object entry in items)
            {
                if (!(entry is Dictionary<string, object> item)) throw run.Error("Each of items: should be a map such as { description: 'Consulting', quantity: 3, price: 450 }.", "items", "Argument");
                object Get(params string[] keys) => keys.Select(k => item.TryGetValue(k, out object v) ? v : null).FirstOrDefault(v => v != null);
                double quantity = Get("quantity", "qty") is object q ? Values.ToNumber(q, "Invoice quantity") : 1;
                object priceValue = Get("price", "unit_price", "rate");
                double price = priceValue != null ? Values.ToNumber(priceValue, "Invoice price") : 0;
                double amount = Get("amount", "total") is object a ? Values.ToNumber(a, "Invoice amount") : quantity * price;
                subtotal += amount;
                var row = new TableRow();
                row.Cells.Add(new TableCell { Inlines = InlineParser.Parse(Values.ToStr(Get("description", "item", "name") ?? string.Empty)) });
                row.Cells.Add(new TableCell { Inlines = new List<Inline> { new TextInline(Format(quantity)) } });
                row.Cells.Add(new TableCell { Inlines = new List<Inline> { new TextInline(priceValue != null ? Money(price, currency) : string.Empty) } });
                row.Cells.Add(new TableCell { Inlines = new List<Inline> { new TextInline(Money(amount, currency)) } });
                table.Rows.Add(row);
            }
            void Total(string label, double amount, bool bold, bool rule)
            {
                var row = new TableRow { RuleAbove = rule };
                row.Cells.Add(new TableCell { Inlines = new List<Inline> { new TextInline(label) }, Span = 3, Bold = bold });
                row.Cells.Add(new TableCell { Inlines = new List<Inline> { new TextInline(Money(amount, currency)) }, Bold = bold });
                table.Rows.Add(row);
            }
            double total = subtotal;
            if (run.Has("tax"))
            {
                double rate = Values.ToNumber(run.Eval("tax"), "Invoice tax");
                double tax = System.Math.Round(subtotal * rate / 100, 2, MidpointRounding.AwayFromZero);
                Total("Subtotal", subtotal, bold: false, rule: true);
                Total($"{Blank(run.Template("tax_name")) ?? "VAT"} at {Format(rate)}%", tax, bold: false, rule: false);
                total += tax;
                Total("Total", total, bold: true, rule: true);
            }
            else
            {
                Total("Total", total, bold: true, rule: true);
            }
            if (run.Has("paid"))
            {
                double paid = Values.ToNumber(run.Eval("paid"), "Invoice paid");
                Total("Paid", paid, bold: false, rule: false);
                Total("Balance due", total - paid, bold: true, rule: true);
            }
            target.Add(new Spacer { Height = 6 });
            target.Add(table);
            string notes = Blank(run.Template("notes"));
            if (notes != null) target.Add(Lines(notes, PaperAlign.Left, spaceBefore: 6));
            return null;
        }
    }
}
