using System.Text;
using KScript.Metadata;
using KScript.Syntax.Text;

namespace KScript.Syntax.Markup
{
    /// <summary>
    /// The one rule for values written in a .kscript argument list, <c>name(key: value)</c>:
    /// <list type="bullet">
    /// <item>"double quotes" are always a string that fills in <c>$name</c> and <c>{ }</c>;</item>
    /// <item>'single quotes' are always a string kept as written;</item>
    /// <item>anything else is code: a value (<c>$rows</c>, <c>[1, 2]</c>, <c>not $ready</c>), a list of names
    /// (<c>[a, b]</c>), statements in braces (<c>{ count += 1 }</c>) or one bare word that names something
    /// (a variable to fill, a function, a type, one of a fixed set of words).</item>
    /// </list>
    /// The parser doesn't know what each argument holds, so it keeps what was written; once the element is known
    /// (<see cref="ElementInfo"/>) this turns each value into the text the element reads, the same text the XML
    /// form holds. The runtime, the editor and the converter all call it, so they can't disagree.
    /// </summary>
    public static class CodeArguments
    {
        /// <summary>
        /// What a type declared without fields is told, by the checker and by the engine: each form of a script gets
        /// the spelling it writes.
        /// </summary>
        public static Wording TypeNeedsFields(string name) => new Wording(
            $"<type name=\"{name}\"> needs fields or methods. Declare <field> or <fn> elements, give fields=\"name: string, age?: int\", or use path=\"sample.json\".",
            $"type {name} needs fields or methods. Declare fields in the body or with fields: [name: string, age?: int], declare a method with fn, or use path: \"sample.json\".");

        /// <summary>
        /// The argument an element takes first without its name in a .kscript file (<c>expect($total, equals: 3)</c>),
        /// or null. XML writes it as an attribute (<c>&lt;expect value="$total" equals="3" /&gt;</c>).
        /// </summary>
        public static string Positional(string element) => ElementInfo.BareName(element ?? string.Empty) switch
        {
            "expect" => "value",
            _ => null,
        };

        public const string NormalizerArity = "A normalize callback takes exactly one parameter.";

        /// <summary>Applies the rule to every argument of every element in a .kscript document.</summary>
        public static void Normalise(MarkupDocument document, Func<string, ElementInfo> lookup)
        {
            if (document == null || document.Syntax != SourceSyntax.Code) return;
            foreach (MarkupElement e in document.DescendantElements())
            {
                Normalise(e, lookup(ElementInfo.BareName(e.Name)));
            }
        }

        /// <summary>Applies the rule to one element's arguments. Safe to call more than once.</summary>
        public static void Normalise(MarkupElement e, ElementInfo info)
        {
            // A server's requests share one parse of a script (ScriptCache), so two may normalise the same element at once.
            lock (e)
            {
                foreach (MarkupAttribute attr in e.Attributes)
                {
                    if (!attr.IsArgument || attr.Normalised || attr.Value == null) continue;
                    AttributeInfo a = info?.GetAttribute(attr.Name);
                    // An argument the element doesn't list (a page variable, a component's own property) is text.
                    AttributeKind kind = a?.Kind ?? AttributeKind.Template;
                    attr.Value = attr.Quote == '"' ? Quoted(attr, kind) : Bare(attr, kind, a);
                    attr.Normalised = true;
                }
            }
        }

        /// <summary>True for arguments whose value is text: written in quotes, or as a value that gives the text.</summary>
        public static bool IsText(AttributeKind kind) =>
            kind is AttributeKind.Template or AttributeKind.Text or AttributeKind.Path or AttributeKind.Selector;

        /// <summary>True for arguments whose value is code that is evaluated.</summary>
        public static bool IsValue(AttributeKind kind) => kind is AttributeKind.Expression or AttributeKind.ExpressionList;

        /// <summary>True for arguments that hold several names or parts, written <c>[a, b]</c>.</summary>
        public static bool IsList(AttributeKind kind, AttributeInfo a = null) =>
            kind is AttributeKind.ExpressionList or AttributeKind.DeclareVariables or AttributeKind.FunctionReferences
                or AttributeKind.TypeNames or AttributeKind.Parameters or AttributeKind.Fields
            || a?.IsNameList == true;

        /// <summary>
        /// True for an argument that names functions, written <c>@name</c> in .kscript (and bare in XML). Two lists
        /// of names are not function references and stay bare: the names an import brings in
        /// (<c>import(names: [a, b])</c>), and middleware, where built-ins, groups and functions share one list
        /// (<c>[log, throttle:30, my_check]</c>).
        /// </summary>
        public static bool TakesFunctions(string element, AttributeInfo a) =>
            a != null && (a.Kind == AttributeKind.FunctionReference
                || (a.Kind == AttributeKind.FunctionReferences && a.Options.Count == 0 && element != "import"));

        /// <summary>The names of a function argument as .kscript writes them (<c>@send</c>, <c>[@a, @b]</c>) or as XML does (<c>send</c>, <c>a, b</c>).</summary>
        public static string FunctionNames(string value, bool code)
        {
            List<string> names = NameList.Names(value).Select(n => n.TrimStart('@')).ToList();
            if (names.Count == 0 || names.Any(n => !Lexer.IsIdentStart(n[0]) || !n.All(c => Lexer.IsIdentChar(c) || c == '.')))
            {
                return value;
            }
            if (!code) return string.Join(", ", names);
            return names.Count == 1 && !NameList.IsArray(value) ? "@" + names[0] : "[" + string.Join(", ", names.Select(n => "@" + n)) + "]";
        }

        private static DecodedText Quoted(MarkupAttribute attr, AttributeKind kind)
        {
            if (IsValue(kind))
            {
                // A string given where a value is read: hand the reader the string literal, quotes and all.
                attr.QuotedValue = true;
                return Literal(attr.Value);
            }
            if (kind == AttributeKind.Statements)
            {
                attr.FormError = $"{attr.Name} takes statements, written in braces: {attr.Name}: {{ {attr.Value.Value.Trim()} }}. Double quotes always make a string.";
            }
            return attr.Value;
        }

        private static DecodedText Bare(MarkupAttribute attr, AttributeKind kind, AttributeInfo a)
        {
            DecodedText value = attr.Value;
            string text = value.Value;
            int start = 0, end = text.Length;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (end == start) return value;
            char first = text[start], last = text[end - 1];

            if (a?.IsNameList == true)
            {
                // [a, b] is read by the list reader, brackets and all.
                return value;
            }
            if (kind == AttributeKind.Statements)
            {
                return first == '{' && last == '}' && Closes(text, start, end) ? Slice(value, start + 1, end - 1) : value;
            }
            if (kind == AttributeKind.ExpressionList || (IsList(kind, a) && a?.IsNameList != true))
            {
                return first == '[' && last == ']' && Closes(text, start, end) ? Slice(value, start + 1, end - 1) : value;
            }
            if (kind == AttributeKind.Bool)
            {
                // true, false, or a condition worked out when it is read: disabled: not $ready.
                return IsBoolWord(text.Substring(start, end - start)) || first == '{' ? value : Wrap(value, start, end);
            }
            if (kind == AttributeKind.Expression)
            {
                return value;
            }
            if (first == '\'' && Quotes.Close(text, start, end) == end - 1 && kind != AttributeKind.Template)
            {
                // 'text' where plain text is read: the text between the quotes.
                attr.QuotedValue = true;
                return Slice(value, start + 1, end - 1);
            }
            if (kind == AttributeKind.Template && IsCode(text, start, end))
            {
                // A value where text is read ($title, 'as written', @name()): it is filled in whole.
                attr.QuotedValue = first is '\'' or '`';
                attr.CodeValue = true;
                return Wrap(value, start, end);
            }
            return value;
        }

        /// <summary>The words a yes/no argument reads directly.</summary>
        public static bool IsBoolWord(string word) =>
            word is "true" or "false" or "yes" or "no" or "on" or "off" or "y" or "n" or "1" or "0";

        /// <summary>True when the text from start to end is one expression that begins as a value does, not a bare word.</summary>
        private static bool IsCode(string text, int start, int end)
        {
            char first = text[start];
            if (!(first is '\'' or '`' or '(' or '[' || ((first is '$' or '@') && start + 1 < end && Lexer.IsIdentStart(text[start + 1]))))
            {
                return false;
            }
            ParseResult parsed = ExpressionParser.ParseExpression(text.Substring(start, end - start));
            return parsed.Errors.Count == 0 && parsed.Root != null;
        }

        /// <summary>True when the bracket at <paramref name="start"/> is closed by the last character before <paramref name="end"/>.</summary>
        private static bool Closes(string text, int start, int end)
        {
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c is '\'' or '"')
                {
                    int close = Quotes.Close(text, i, end);
                    if (close < 0) return false;
                    i = close;
                }
                else if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}')
                {
                    depth--;
                    if (depth == 0) return i == end - 1;
                }
            }
            return false;
        }

        private static DecodedText Slice(DecodedText value, int start, int end)
        {
            int[] map = new int[end - start + 1];
            for (int i = 0; i < map.Length; i++) map[i] = value.ToSource(start + i);
            return new DecodedText(value.Value.Substring(start, end - start), map);
        }

        private static DecodedText Wrap(DecodedText value, int start, int end)
        {
            StringBuilder sb = new StringBuilder();
            List<int> map = new List<int>();
            sb.Append('{');
            map.Add(value.ToSource(start));
            for (int i = start; i < end; i++)
            {
                sb.Append(value.Value[i]);
                map.Add(value.ToSource(i));
            }
            sb.Append('}');
            map.Add(value.ToSource(end));
            map.Add(value.ToSource(end));
            return new DecodedText(sb.ToString(), map.ToArray());
        }

        /// <summary>The decoded text of a "string" written back as the string literal an expression reads.</summary>
        private static DecodedText Literal(DecodedText value)
        {
            string text = value.Value;
            StringBuilder sb = new StringBuilder(text.Length + 2);
            List<int> map = new List<int>(text.Length + 3);
            int open = Math.Max(0, value.ToSource(0) - 1);
            sb.Append('"');
            map.Add(open);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"' || (c == '\\' && (i + 1 == text.Length || text[i + 1] is '"' or '\\')))
                {
                    sb.Append('\\');
                    map.Add(value.ToSource(i));
                }
                sb.Append(c);
                map.Add(value.ToSource(i));
            }
            sb.Append('"');
            map.Add(value.ToSource(text.Length));
            map.Add(value.ToSource(text.Length) + 1);
            return new DecodedText(sb.ToString(), map.ToArray());
        }
    }
}
