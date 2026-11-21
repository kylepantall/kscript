using System.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// A documentation comment: Markdown text with optional tags, written as <c>///</c> lines before a <c>fn</c> or
    /// <c>const</c> in a .kscript file, a <c>&lt;!--- ... --&gt;</c> comment before the element in XML, or a
    /// <c>description</c> attribute. Editors show it in hover, completion and signature help.
    /// <code>
    /// /// Greets someone.
    /// /// @param who The person to greet.
    /// /// @arg greeting How to start, such as 'Hi'.
    /// /// @returns The greeting, ending with '!'.
    /// /// @example
    /// ///   @greet('Kyle')   // Hello, Kyle!
    /// /// @deprecated Use @welcome instead.
    /// </code>
    /// Coding agents get their own tags, which <c>kscript agents</c> turns into compact docs (AGENTS.md and
    /// .kscript/agents): <c>@llm</c> is the one line an agent reads, <c>@prefer</c> and <c>@avoid</c> say which
    /// way to write something, <c>@pitfall</c> names a mistake that is easy to make, and <c>@agent hide</c> leaves
    /// the symbol out of the agent docs. <c>@arg</c> and <c>@argument</c> are other names for <c>@param</c>, and
    /// <c>@return</c> for <c>@returns</c>.
    /// </summary>
    public sealed class DocComment
    {
        private DocComment()
        {
        }

        /// <summary>The text before the first tag (Markdown).</summary>
        public string Summary { get; private set; } = string.Empty;

        /// <summary>@param descriptions, by parameter name (without $), in the order written.</summary>
        public IReadOnlyList<(string Name, string Text)> Params => paramList;

        public string Returns { get; private set; }

        /// <summary>The @deprecated text: empty when deprecated without a reason, null when not deprecated.</summary>
        public string Deprecated { get; private set; }

        /// <summary>
        /// True when the doc says <c>@public</c>: the function is part of an API used from outside the project, so it
        /// is never reported as unused (KS6002) even when nothing in the project calls it.
        /// </summary>
        public bool IsPublic { get; private set; }

        public IReadOnlyList<string> Examples => examples;

        public IReadOnlyList<string> SeeAlso => see;

        /// <summary>The <c>@llm</c> line: what a coding agent needs to know, in one line. Null when not given.</summary>
        public string Llm { get; private set; }

        /// <summary><c>@prefer</c> lines: the way to write something that agents should choose.</summary>
        public IReadOnlyList<string> Prefer => prefer;

        /// <summary><c>@avoid</c> lines: ways of using the symbol that agents should not choose.</summary>
        public IReadOnlyList<string> Avoid => avoid;

        /// <summary><c>@pitfall</c> lines: mistakes that are easy to make with the symbol.</summary>
        public IReadOnlyList<string> Pitfalls => pitfalls;

        /// <summary>True when the doc says <c>@agent hide</c>: the symbol is left out of the agent docs.</summary>
        public bool AgentHidden { get; private set; }

        public bool IsEmpty => Summary.Length == 0 && paramList.Count == 0 && Returns == null && Deprecated == null && !IsPublic && examples.Count == 0 && see.Count == 0
            && Llm == null && prefer.Count == 0 && avoid.Count == 0 && pitfalls.Count == 0 && !AgentHidden;

        private readonly List<(string, string)> paramList = new List<(string, string)>();
        private readonly List<string> examples = new List<string>();
        private readonly List<string> see = new List<string>();
        private readonly List<string> prefer = new List<string>();
        private readonly List<string> avoid = new List<string>();
        private readonly List<string> pitfalls = new List<string>();

        public string ParamText(string name) => paramList.FirstOrDefault(p => p.Item1 == name).Item2;

        /// <summary>
        /// The tags a doc comment understands, for completion, hover and highlighting: the name (without @), what
        /// follows it, what it means, whether it may be written more than once, and the tag it is another name for.
        /// </summary>
        public static IReadOnlyList<DocTag> KnownTags { get; } = new[]
        {
            new DocTag("param", "name text", "Describes a parameter: its name (the $ is optional), then what it is for.", Repeats: true),
            new DocTag("arg", "name text", "Another name for @param: describes a parameter.", Repeats: true, AliasOf: "param"),
            new DocTag("argument", "name text", "Another name for @param: describes a parameter.", Repeats: true, AliasOf: "param"),
            new DocTag("returns", "text", "Describes the result."),
            new DocTag("return", "text", "Another name for @returns: describes the result.", AliasOf: "returns"),
            new DocTag("example", "code", "Example code, on the same line or on the indented lines below. Hover shows it as a code block.", Repeats: true),
            new DocTag("deprecated", "text", "Marks the symbol as deprecated, with what to use instead. Calls to it get a warning (KS4008)."),
            new DocTag("see", "text", "Something related, such as @welcome or a link.", Repeats: true),
            new DocTag("public", string.Empty, "The function is used from outside the project, so it is never reported as unused (KS6002)."),
            new DocTag("llm", "text", "The one line a coding agent reads about the symbol (kscript agents)."),
            new DocTag("prefer", "text", "The way of writing something that coding agents should choose.", Repeats: true),
            new DocTag("avoid", "text", "A way of using the symbol that coding agents should not choose.", Repeats: true),
            new DocTag("pitfall", "text", "A mistake that is easy to make with the symbol.", Repeats: true),
            new DocTag("agent", "hide", "@agent hide leaves the symbol out of the agent docs."),
        };

        /// <summary>The known tag called <paramref name="name"/> (without @), or null.</summary>
        public static DocTag FindTag(string name) => KnownTags.FirstOrDefault(t => t.Name == name);

        /// <summary>True for @param and its other names, @arg and @argument.</summary>
        public static bool IsParamTag(string name) => FindTag(name) is DocTag t && (t.Name == "param" || t.AliasOf == "param");

        // Longest first, so @argument is not read as @arg followed by "ument".
        private static readonly string[] Tags = KnownTags.Select(t => t.Name).OrderByDescending(n => n.Length).ToArray();

        /// <summary>Parses doc text. Returns null for null or blank text.</summary>
        public static DocComment Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            DocComment doc = new DocComment();
            StringBuilder summary = new StringBuilder();
            StringBuilder current = summary;
            Action<string> finish = null;
            bool inTag = false;
            bool inExample = false;

            void Close()
            {
                finish?.Invoke(current.ToString());
                finish = null;
            }

            foreach (string raw in Dedent(text.Replace("\r\n", "\n").Split('\n')))
            {
                string line = raw.TrimEnd();
                string trimmed = line.TrimStart();
                string tag = trimmed.StartsWith("@", StringComparison.Ordinal) ? Tags.FirstOrDefault(t => trimmed.Length > t.Length + 1
                    ? trimmed.Substring(1).StartsWith(t, StringComparison.Ordinal) && char.IsWhiteSpace(trimmed[t.Length + 1])
                    : trimmed.Substring(1) == t) : null;
                if (tag == null)
                {
                    if (current.Length > 0 || line.Length > 0)
                    {
                        // A tag's text may wrap onto indented lines; examples keep their indentation.
                        current.Append(inTag && !inExample ? line.Trim() : line).Append('\n');
                    }
                    continue;
                }

                Close();
                inTag = true;
                inExample = tag == "example";
                string rest = trimmed.Length > tag.Length + 1 ? trimmed.Substring(tag.Length + 1).Trim() : string.Empty;
                current = new StringBuilder();
                switch (FindTag(tag).AliasOf ?? tag)
                {
                    case "param":
                        {
                            int space = rest.IndexOfAny(new[] { ' ', '\t' });
                            string name = (space < 0 ? rest : rest.Substring(0, space)).TrimStart('$');
                            current.Append(space < 0 ? string.Empty : rest.Substring(space + 1).Trim()).Append('\n');
                            finish = t => doc.paramList.Add((name, Clean(t)));
                            break;
                        }
                    case "returns":
                        current.Append(rest).Append('\n');
                        finish = t => doc.Returns = Clean(t);
                        break;
                    case "deprecated":
                        current.Append(rest).Append('\n');
                        finish = t => doc.Deprecated = Clean(t);
                        break;
                    case "public":
                        doc.IsPublic = true;
                        break;
                    case "see":
                        current.Append(rest).Append('\n');
                        finish = t => doc.see.Add(Clean(t));
                        break;
                    case "llm":
                        current.Append(rest).Append('\n');
                        finish = t => doc.Llm = OneLine(t);
                        break;
                    case "prefer":
                    case "avoid":
                    case "pitfall":
                        {
                            List<string> into = tag == "prefer" ? doc.prefer : tag == "avoid" ? doc.avoid : doc.pitfalls;
                            current.Append(rest).Append('\n');
                            finish = t => into.Add(OneLine(t));
                            break;
                        }
                    case "agent":
                        doc.AgentHidden |= string.Equals(rest, "hide", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "example":
                        if (rest.Length > 0)
                        {
                            current.Append(rest).Append('\n');
                        }
                        finish = t => doc.examples.Add(string.Join("\n", Dedent(t.TrimEnd().Split('\n'))).Trim('\n'));
                        break;
                }
            }
            Close();
            doc.Summary = Clean(summary.ToString());
            return doc.IsEmpty ? null : doc;
        }

        /// <summary>Joins a tag's lines into one paragraph unless they hold Markdown structure.</summary>
        private static string Clean(string text) => text.Trim();

        /// <summary>Agent tags are read as one line: wrapped lines are joined with spaces.</summary>
        private static string OneLine(string text) => string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        private static IEnumerable<string> Dedent(IList<string> lines)
        {
            int indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            return lines.Select(l => l.Length >= indent ? l.Substring(indent) : l.TrimStart());
        }

        /// <summary>The whole comment as Markdown for a hover: deprecation, summary, parameters, result, examples.</summary>
        public string ToMarkdown(bool includeParams = true)
        {
            StringBuilder sb = new StringBuilder();
            void Block(string s)
            {
                if (sb.Length > 0)
                {
                    sb.Append("\n\n");
                }
                sb.Append(s);
            }

            if (Deprecated != null)
            {
                Block("**Deprecated.**" + (Deprecated.Length > 0 ? " " + Deprecated : string.Empty));
            }
            if (Summary.Length > 0)
            {
                Block(Summary);
            }
            if (includeParams && paramList.Count > 0)
            {
                Block("**Parameters**\n" + string.Join("\n", paramList.Select(p => $"- `{p.Item1}`" + (p.Item2.Length > 0 ? ": " + p.Item2.Replace("\n", " ") : string.Empty))));
            }
            if (Returns != null)
            {
                Block("**Returns** " + Returns);
            }
            foreach (string example in examples)
            {
                Block("**Example**\n```kscript\n" + example + "\n```");
            }
            if (prefer.Count > 0)
            {
                Block(string.Join("\n", prefer.Select(p => "**Prefer** " + p)));
            }
            if (avoid.Count > 0)
            {
                Block(string.Join("\n", avoid.Select(p => "**Avoid** " + p)));
            }
            if (pitfalls.Count > 0)
            {
                Block(string.Join("\n", pitfalls.Select(p => "**Pitfall** " + p)));
            }
            if (see.Count > 0)
            {
                Block("**See also** " + string.Join(", ", see));
            }
            return sb.ToString();
        }
    }

    /// <summary>A tag a doc comment understands, such as <c>@param</c>.</summary>
    /// <param name="Name">The tag without its @.</param>
    /// <param name="Takes">What follows the tag, such as "name text"; empty when nothing does.</param>
    /// <param name="Description">What the tag means, shown in completion and hover.</param>
    /// <param name="Repeats">True when a comment may hold the tag more than once.</param>
    /// <param name="AliasOf">The tag this one is another name for, or null.</param>
    public sealed record DocTag(string Name, string Takes, string Description, bool Repeats = false, string AliasOf = null);
}
