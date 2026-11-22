using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

using KScript.Syntax.Markup;

namespace KScript.Syntax
{
    /// <summary>
    /// A message that reads right in both forms of a script. It is written once, in XML terms
    /// (<c>$"&lt;{name}&gt; needs a {attribute} attribute."</c>), and a .kscript file gets it in its own terms:
    /// <c>throw</c> instead of <c>&lt;throw&gt;</c>, <c>status: 404</c> instead of <c>status="404"</c>, "statement" and
    /// "argument" instead of "element" and "attribute".
    /// <para>
    /// The compiler builds one from an interpolated string, so only the words written in the source are reworded:
    /// the values put into the holes (a name, a value, a user's own text) stay exactly as they are. A plain string
    /// given where a Wording is expected is final text and is never reworded.
    /// </para>
    /// </summary>
    [InterpolatedStringHandler]
    public sealed class Wording
    {
        private const char Open = '\uE000';
        private const char Close = '\uE001';

        private readonly StringBuilder template = new StringBuilder();
        private readonly List<object> holes = new List<object>();
        private string xml;
        private string code;
        private string snippet;

        public Wording(int literalLength, int formattedCount)
        {
        }

        private Wording(string final)
        {
            xml = final;
            code = final;
        }

        /// <summary>A message with its own words for each form, when rewording wouldn't say it well.</summary>
        public Wording(string xml, string code)
        {
            this.xml = xml;
            this.code = code;
        }

        /// <summary>The message an interpolated string writes: <c>Wording.Of($"&lt;{name}&gt; has no body.")</c>.</summary>
        public static Wording Of(Wording message) => message;

        /// <summary>
        /// Text written in XML terms somewhere else, such as an attribute's description in the catalog: it is reworded
        /// for a .kscript file as a message's own words are.
        /// </summary>
        public static Wording Prose(string text)
        {
            if (text == null) return null;
            var wording = new Wording(text.Length, 0);
            wording.AppendLiteral(text);
            return wording;
        }

        /// <summary>A line of XML script, such as an element's example: a .kscript file gets it converted.</summary>
        public static Wording Snippet(string xmlLine)
        {
            if (xmlLine == null) return null;
            var wording = new Wording(xmlLine) { code = null, snippet = xmlLine };
            return wording;
        }

        /// <summary>Text that is already final and must not be reworded, such as a message a script wrote.</summary>
        public static Wording Final(string text) => text == null ? null : new Wording(text);

        public static implicit operator Wording(string text) => Final(text);

        /// <summary>Two messages one after the other. Null adds nothing.</summary>
        public static Wording operator +(Wording a, Wording b)
        {
            if (a == null) return b;
            if (b == null) return a;
            var joined = new Wording(0, 0);
            joined.AppendFormatted(a);
            joined.AppendFormatted(b);
            return joined;
        }

        public void AppendLiteral(string text) => template.Append(text);

        public void AppendFormatted<T>(T value)
        {
            template.Append(Open).Append(holes.Count).Append(Close);
            holes.Add(value);
        }

        public void AppendFormatted<T>(T value, string format) =>
            AppendFormatted(value is IFormattable f ? f.ToString(format, null) : value?.ToString());

        public void AppendFormatted<T>(T value, int alignment) => AppendFormatted(value, alignment, null);

        public void AppendFormatted<T>(T value, int alignment, string format)
        {
            string text = value is IFormattable f ? f.ToString(format, null) : value?.ToString() ?? string.Empty;
            AppendFormatted(alignment < 0 ? text.PadRight(-alignment) : text.PadLeft(alignment));
        }

        /// <summary>The message as written, for an XML script or when the form isn't known.</summary>
        public string Xml => xml ??= Fill(template.ToString(), false);

        /// <summary>The message for a .kscript file.</summary>
        public string Code => code ??= snippet != null ? SnippetAsCode(snippet) : ForCode();

        /// <summary>The message for a file of the given form.</summary>
        public string For(bool isCode) => isCode ? Code : Xml;

        public override string ToString() => Xml;

        // ---- filling the holes ----

        private static readonly Regex Hole = new Regex(Open + @"(\d+)" + Close, RegexOptions.Compiled);

        private string Fill(string text, bool isCode) => Hole.Replace(text, m =>
        {
            object value = holes[int.Parse(m.Groups[1].Value)];
            return value is Wording nested ? nested.For(isCode) : value?.ToString() ?? string.Empty;
        });

        // ---- rewording ----

        // <name>, </name>, <name />, <name attr>, <name a="1" b="{ x }">: a name may be (or hold) a hole.
        private static readonly Regex Tag = new Regex(
            "</?(?<name>[A-Za-z" + Open + "][\\w.:\\-" + Open + Close + "]*)(?<attrs>(?:\\s+[\\w:\\-" + Open + Close + "]+(?:=\"[^\"]*\"|='[^']*')?)*)(?:\\s+\\.\\.\\.)?\\s*/?>",
            RegexOptions.Compiled);

        private static readonly Regex Element = new Regex(
            "<(?<name>[A-Za-z][\\w.\\-]*)(?:\\s+[\\w:\\-]+=\"[^\"]*\")*\\s*>[^<>" + Open + "]+</\\k<name>>", RegexOptions.Compiled);

        private static readonly Regex XmlAside = new Regex(@"\s*\(in (?:an )?XML\b[^()]*\)", RegexOptions.Compiled);

        private static readonly Regex Attribute = new Regex("(?<name>[\\w:\\-" + Open + Close + "]+)(?:=(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'))?", RegexOptions.Compiled);

        // attr="value" on its own, outside a tag.
        private static readonly Regex Pair = new Regex("(?<![\\w" + Close + "])(?<name>[A-Za-z" + Open + "][\\w\\-" + Open + Close + "]*)=\"(?<value>[^\"]*)\"", RegexOptions.Compiled);

        private static readonly Regex Words = new Regex(@"\b(?<article>[Aa]n )?(?<word>[Aa]ttribute|[Ee]lement)(?<plural>s?)\b", RegexOptions.Compiled);

        private string ForCode()
        {
            // Pieces that are finished (a converted tag, a filled hole) are set aside, so nothing rewords them again.
            var done = new List<string>();
            string Keep(string text)
            {
                done.Add(text);
                return Open.ToString() + "k" + (done.Count - 1) + Close;
            }

            // "(in XML, <for each="n">...)" is an aside for XML scripts: a .kscript file doesn't need it.
            string written = XmlAside.Replace(template.ToString(), string.Empty);
            // A whole element with text in it (<llm to="answer">What is KScript?</llm>) is one statement.
            string text = Element.Replace(written, m => WholeAsCode(Fill(m.Value, true)) is string whole ? Keep(whole) : m.Value);
            text = Tag.Replace(text, m => Keep(TagAsCode(m)));
            text = Pair.Replace(text, m => Keep(Fill(m.Groups["name"].Value, true) + ": " + ValueAsCode(Fill(m.Groups["value"].Value, true))));
            text = Words.Replace(text, m =>
            {
                bool upper = char.IsUpper(m.Value[0]);
                string word = char.ToLowerInvariant(m.Groups["word"].Value[0]) == 'a' ? "argument" : "statement";
                // "an element" is "a statement"; "an attribute" stays "an argument".
                string article = !m.Groups["article"].Success ? string.Empty : word == "statement" ? "a " : "an ";
                string all = article + word + m.Groups["plural"].Value;
                return upper ? char.ToUpperInvariant(all[0]) + all.Substring(1) : all;
            });
            text = Fill(text, true);
            return Regex.Replace(text, Open + @"k(\d+)" + Close, m => done[int.Parse(m.Groups[1].Value)]);
        }

        private string TagAsCode(Match tag)
        {
            string name = Fill(tag.Groups["name"].Value, true);
            var pairs = new List<(string Name, string Value)>();
            foreach (Match a in Attribute.Matches(tag.Groups["attrs"].Value))
            {
                pairs.Add((Fill(a.Groups["name"].Value, true), a.Groups["value"].Success ? Fill(a.Groups["value"].Value, true) : null));
            }
            if (pairs.Count == 0) return name;

            // <throw value> names an argument of a statement: "throw value:".
            if (pairs.TrueForAll(p => p.Value == null)) return name + " " + string.Join(" ", pairs.ConvertAll(p => p.Name + ":"));

            return Converted(name, pairs) ?? name + " " + string.Join(", ", pairs.ConvertAll(p => p.Value == null ? p.Name + ":" : p.Name + ": " + ValueAsCode(p.Value)));
        }

        /// <summary>A line of XML script as .kscript: converted when it is whole, reworded tag by tag otherwise.</summary>
        private static string SnippetAsCode(string line) => WholeAsCode(line) ?? Prose(line).Code;

        /// <summary>Whole elements as the converter writes them in .kscript, on one line, or null when they aren't whole.</summary>
        private static string WholeAsCode(string xml)
        {
            try
            {
                MarkupDocument document = MarkupParser.Parse("<kscript>" + xml + "</kscript>", DocumentKind.Script);
                if (document.Diagnostics.Count > 0) return null;
                string text = ScriptConverter.ToCode(document).Text?.Trim();
                return string.IsNullOrEmpty(text) || text.Contains('<') ? null : text.Replace("\r", string.Empty).Replace("\n", " ");
            }
            catch (Exception)
            {
                // Not a whole statement, such as the first line of a longer example.
                return null;
            }
        }

        /// <summary>The tag as the converter writes it in .kscript, or null when it can't be read as a statement.</summary>
        private static string Converted(string name, List<(string Name, string Value)> pairs)
        {
            if (pairs.Exists(p => p.Value == null)) return null;
            try
            {
                var source = new StringBuilder("<kscript><").Append(name);
                foreach ((string attribute, string value) in pairs)
                {
                    source.Append(' ').Append(attribute).Append("=\"").Append(value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;")).Append('"');
                }
                source.Append(" /></kscript>");
                MarkupDocument document = MarkupParser.Parse(source.ToString(), DocumentKind.Script);
                if (document.Diagnostics.Count > 0) return null;
                ConversionResult result = ScriptConverter.ToCode(document);
                string text = result.Text?.Trim();
                if (string.IsNullOrEmpty(text) || result.Warnings.Count > 0 || text.Contains('\n')) return null;
                // A statement with a body is shown without its empty braces: "for x in $list", not "for x in $list { }".
                text = Regex.Replace(text, @"\s*\{\s*\}$", string.Empty);
                return text.Contains('<') ? null : text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// An attribute's value as an argument: values and names as they are, other text in quotes. A value that is
        /// only a placeholder (three dots) is left alone.
        /// </summary>
        internal static string ValueAsCode(string value)
        {
            string v = value.Trim();
            if (v.Length == 0) return "\"\"";
            if (v == "..." || v == "…") return v;
            bool expression = v[0] is '$' or '@' or '[' or '(' or '{' or '\'' or '-' || char.IsDigit(v[0])
                || v is "true" or "false" or "null"
                || Regex.IsMatch(v, @"^[A-Za-z_][\w.]*(\(.*\))?$");
            return expression ? v : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
