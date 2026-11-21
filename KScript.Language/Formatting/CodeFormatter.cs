using System.Text;

using KScript.Syntax;
using KScript.Syntax.Markup;
using KScript.Syntax.Text;

namespace KScript.Language.Formatting
{
    /// <summary>
    /// Tidies <c>.kscript</c> files (the brace-and-newline format).
    /// <para>
    /// The formatter works on the source text, with a scanner that reads strings, comments and brackets exactly as
    /// <see cref="CodeParser"/> does, and it only ever changes whitespace and statement separators:
    /// indentation by nesting (one level per line that opens brackets, one more for a statement carried onto the next
    /// line), one statement per line (<c>a = 1; b = 2</c> and one-line blocks are split), <c>} else {</c> on one line,
    /// spaces after commas and after the colon of <c>name: value</c>, around assignments and before a block's
    /// <c>{</c>, long argument lists, arrays and maps wrapped one item per line, trailing spaces, blank lines and the
    /// final newline.
    /// </para>
    /// <para>
    /// Text in <c>"double quotes"</c>, <c>'strings'</c> and comments is never changed (a multi-line <c>/* */</c>
    /// comment moves as a block). Every result is parsed again and compared with the original element tree; if the
    /// meaning would change, only the line layout is tidied, and if even that would change it the file is left
    /// alone with an error. <c>// kscript-fmt: off</c> … <c>// kscript-fmt: on</c> and <c>// kscript-fmt-ignore</c>
    /// keep parts of a file exactly.
    /// </para>
    /// </summary>
    public static class CodeFormatter
    {
        private static readonly string[] TrailingContinuations =
        {
            "|>", "->", "=>", "??", "==", "!=", ">=", "<=", "+", "-", "*", "/", "%", ",", "?", ":", "=", "<", ">", "..",
        };

        private static readonly string[] LeadingContinuations = { "|>", "->", "??", "?.", "?", ":", "." };

        private static readonly string[] WordContinuations = { "and", "or", "not", "step", "in" };

        /// <summary>Words after which a '{' starts a map, not a block.</summary>
        private static readonly HashSet<string> ValueWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "return", "echo", "throw", "exit", "and", "or", "not", "in", "step", "when", "is",
        };

        /// <summary>Words that may follow a block's '}' on the same line.</summary>
        private static readonly HashSet<string> ChainWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "else", "elif", "catch", "finally",
        };

        public static FormatResult Format(string text, FormatOptions options = null)
        {
            options ??= FormatOptions.Default;
            text ??= string.Empty;
            string bom = text.Length > 0 && text[0] == '﻿' ? "﻿" : string.Empty;
            string body = text.Substring(bom.Length);

            MarkupDocument original = CodeParser.Parse(body);
            Diagnostic error = original.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (error != null)
            {
                LinePosition at = original.Source.GetPosition(error.Span.Start);
                return new FormatResult
                {
                    Error = $"not formatted: line {at.Line + 1}: {error.Message}",
                    ErrorSpan = new TextSpan(error.Span.Start + bom.Length, error.Span.Length),
                };
            }

            string eol = options.EndOfLine switch
            {
                EndOfLine.Lf => "\n",
                EndOfLine.CrLf => "\r\n",
                _ => DominantEndOfLine(body),
            };
            string lf = body.Replace("\r\n", "\n");

            // The full set of rules first; if the result means something else, only the line layout; else nothing.
            string where = null;
            foreach (bool full in new[] { true, false })
            {
                string formatted = Run(lf, options, full);
                if (Tamper != null) formatted = Tamper(formatted);
                if (CodeMeaning.Same(original, CodeParser.Parse(formatted), out where))
                {
                    return new FormatResult { Text = bom + (eol == "\n" ? formatted : formatted.Replace("\n", eol)) };
                }
            }
            return new FormatResult { Error = $"not formatted: tidying this file would change what it does ({where}). Please report it." };
        }

        /// <summary>Tests only: changes each result before the meaning check, to show what happens when a result means something else.</summary>
        [ThreadStatic]
        internal static Func<string, string> Tamper;

        private static string Run(string text, FormatOptions options, bool full)
        {
            if (full && (options.StatementPerLine || options.BraceStyle))
            {
                text = Expand(text, options);
            }
            if (full && options.Spacing)
            {
                text = Space(text);
            }
            text = Layout(text, options);
            if (full && (options.WrapAttributes || options.WrapStrings))
            {
                for (int i = 0; i < 24; i++)
                {
                    string wrapped = Wrap(text, options);
                    if (wrapped == text) break;
                    text = Layout(wrapped, options);
                }
            }
            return text;
        }

        #region Scanning

        private enum State : byte
        {
            Code,
            Single,
            Double,
            LineComment,
            BlockComment,
        }

        /// <summary>The source read the way <see cref="CodeParser"/> reads it: what each character is, and which brackets match.</summary>
        private sealed class Scan
        {
            public string Text;
            public State[] States;
            public int[] Match;          // for an opener or closer, the index of its partner (or -1)
            public bool[] IsBlock;       // for a '{' or '}': it opens or closes a block of statements
            public bool[] InExpression;  // for a code char: some enclosing bracket is ( [ or a map's {
            public List<int> LineStarts = new List<int>();
            public bool[] Verbatim;      // characters inside kscript-fmt: off regions and ignored statements
            public bool[] Joined;        // a '\n' in a string after a \ that joins the line to the next
            public List<StringBlock> Blocks = new List<StringBlock>(); // ``` blocks, in order

            public bool IsCode(int i) => i >= 0 && i < Text.Length && States[i] == State.Code;

            public int LineEnd(int line) => line + 1 < LineStarts.Count ? LineStarts[line + 1] - 1 : Text.Length;
        }

        private static Scan Read(string text)
        {
            Scan s = new Scan
            {
                Text = text,
                States = new State[text.Length],
                Match = Enumerable.Repeat(-1, text.Length).ToArray(),
                IsBlock = new bool[text.Length],
                InExpression = new bool[text.Length],
                Verbatim = new bool[text.Length],
                Joined = new bool[text.Length],
            };
            s.LineStarts.Add(0);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') s.LineStarts.Add(i + 1);
            }

            State state = State.Code;
            Stack<int> open = new Stack<int>();
            int expressionDepth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (state)
                {
                    case State.Code:
                        s.States[i] = State.Code;
                        s.InExpression[i] = expressionDepth > 0;
                        if (c == '/' && Peek(text, i + 1) == '/')
                        {
                            state = State.LineComment;
                            s.States[i] = State.LineComment;
                        }
                        else if (c == '/' && Peek(text, i + 1) == '*')
                        {
                            state = State.BlockComment;
                            s.States[i] = State.BlockComment;
                            s.States[i + 1] = State.BlockComment;
                            i++;
                        }
                        else if (c == '\'' || c == '"')
                        {
                            state = c == '\'' ? State.Single : State.Double;
                            s.States[i] = state;
                        }
                        else if (c == '`' && StringBlock.Read(text, i) is StringBlock fence)
                        {
                            // A ``` block reads as one string, fences included.
                            for (int j = i; j < fence.End; j++) s.States[j] = State.Double;
                            s.Blocks.Add(fence);
                            i = fence.End - 1;
                        }
                        else if (c == '(' || c == '[' || c == '{')
                        {
                            bool block = c == '{' && expressionDepth == 0 && OpensBlock(s, text, i);
                            s.IsBlock[i] = block;
                            if (!block) expressionDepth++;
                            open.Push(i);
                        }
                        else if ((c == ')' || c == ']' || c == '}') && open.Count > 0)
                        {
                            int o = open.Pop();
                            s.Match[o] = i;
                            s.Match[i] = o;
                            s.IsBlock[i] = s.IsBlock[o];
                            if (!s.IsBlock[o]) expressionDepth--;
                            s.InExpression[i] = expressionDepth > 0;
                        }
                        break;
                    case State.Single:
                    case State.Double:
                        s.States[i] = state;
                        if (c == '\\' && i + 1 < text.Length)
                        {
                            if (text[i + 1] == '\n') s.Joined[i + 1] = true;
                            s.States[++i] = state;
                        }
                        else if (c == (state == State.Single ? '\'' : '"'))
                        {
                            state = State.Code;
                        }
                        break;
                    case State.LineComment:
                        if (c == '\n')
                        {
                            state = State.Code;
                            s.States[i] = State.Code;
                            s.InExpression[i] = expressionDepth > 0;
                        }
                        else s.States[i] = State.LineComment;
                        break;
                    case State.BlockComment:
                        s.States[i] = State.BlockComment;
                        if (c == '*' && Peek(text, i + 1) == '/')
                        {
                            s.States[++i] = State.BlockComment;
                            state = State.Code;
                        }
                        break;
                }
            }

            MarkVerbatim(s);
            return s;
        }

        private static char Peek(string text, int i) => i >= 0 && i < text.Length ? text[i] : '\0';

        /// <summary>
        /// Whether a '{' (not inside brackets) opens a block of statements rather than a map literal: it follows a
        /// statement's head (a name, ')', ']', a "string" or a word such as else), not an operator or a value keyword.
        /// </summary>
        private static bool OpensBlock(Scan s, string text, int brace)
        {
            int p = brace - 1;
            while (p >= 0 && (text[p] == ' ' || text[p] == '\t')) p--;
            if (p < 0 || text[p] == '\n') return false;
            char c = text[p];
            if (c == ')' || c == ']' || c == '"') return true;
            if (!Lexer.IsIdentChar(c)) return false;
            int end = p + 1;
            while (p >= 0 && (Lexer.IsIdentChar(text[p]) || text[p] == '.' || text[p] == '-')) p--;
            string word = text.Substring(p + 1, end - p - 1);
            if (ValueWords.Contains(word)) return false;
            // enum Size { small, medium } holds a list of names, not statements: leave its layout alone.
            int lineStart = text.LastIndexOf('\n', Math.Max(0, brace - 1)) + 1;
            string head = text.Substring(lineStart, brace - lineStart).TrimStart();
            if (head.StartsWith("enum ", StringComparison.Ordinal)) return false;
            // x = match $choice { ... } is a value, not a block: an assignment before the brace makes it one.
            return !HasTopLevelAssignment(text, lineStart, brace);
        }

        private static bool HasTopLevelAssignment(string text, int start, int end)
        {
            int depth = 0;
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c == '\'' || c == '"')
                {
                    i++;
                    while (i < end && text[i] != c)
                    {
                        if (text[i] == '\\') i++;
                        i++;
                    }
                    continue;
                }
                if (c == '/' && Peek(text, i + 1) == '/') return false;
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (depth == 0 && IsAssignment(text, i, out int length))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The comments that switch formatting off for part of a file.</summary>
        public const string OffDirective = "kscript-fmt: off";
        public const string OnDirective = "kscript-fmt: on";
        public const string IgnoreDirective = "kscript-fmt-ignore";

        private static string Directive(Scan s, int line)
        {
            int start = s.LineStarts[line], end = s.LineEnd(line);
            int i = start;
            while (i < end && (s.Text[i] == ' ' || s.Text[i] == '\t')) i++;
            if (i + 2 > end || s.Text[i] != '/' || s.Text[i + 1] != '/' || s.States[i] != State.LineComment) return null;
            string body = string.Join(" ", s.Text.Substring(i + 2, end - i - 2).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (string.Equals(body, OffDirective, StringComparison.OrdinalIgnoreCase) || string.Equals(body, "kscript-fmt:off", StringComparison.OrdinalIgnoreCase)) return OffDirective;
            if (string.Equals(body, OnDirective, StringComparison.OrdinalIgnoreCase) || string.Equals(body, "kscript-fmt:on", StringComparison.OrdinalIgnoreCase)) return OnDirective;
            if (body.StartsWith(IgnoreDirective, StringComparison.OrdinalIgnoreCase)) return IgnoreDirective;
            return null;
        }

        /// <summary>
        /// Marks what the ignore comments cover. From <c>// kscript-fmt: off</c> to <c>// kscript-fmt: on</c> (or the end
        /// of the block the comment is in), every line is kept exactly; <c>// kscript-fmt-ignore</c> keeps the next
        /// statement, including any block it opens.
        /// </summary>
        private static void MarkVerbatim(Scan s)
        {
            for (int line = 0; line < s.LineStarts.Count; line++)
            {
                string d = Directive(s, line);
                if (d == OffDirective)
                {
                    int startLine = line + 1;
                    int depth = Depth(s, s.LineStarts[line]);
                    int l = startLine;
                    for (; l < s.LineStarts.Count; l++)
                    {
                        if (Directive(s, l) == OnDirective) break;
                        // A line that closes the block the comment is in ends the region (that line is formatted).
                        if (Depth(s, s.LineEnd(l)) < depth) break;
                    }
                    Mark(s, startLine, l - 1);
                    line = l;
                }
                else if (d == IgnoreDirective)
                {
                    int l = line + 1;
                    while (l < s.LineStarts.Count && (IsBlankLine(s, l) || Directive(s, l) != null || IsCommentLine(s, l))) l++;
                    if (l >= s.LineStarts.Count) continue;
                    int depth = Depth(s, s.LineStarts[l]);
                    int last = l;
                    while (last + 1 < s.LineStarts.Count && Depth(s, s.LineEnd(last)) > depth) last++;
                    Mark(s, l, last);
                    line = last;
                }
            }
        }

        private static void Mark(Scan s, int fromLine, int toLine)
        {
            if (toLine < fromLine) return;
            int start = s.LineStarts[fromLine];
            int end = Math.Min(s.Text.Length, s.LineEnd(toLine) + 1);
            for (int i = start; i < end; i++) s.Verbatim[i] = true;
        }

        /// <summary>How many brackets are open just before an offset.</summary>
        private static int Depth(Scan s, int offset)
        {
            int depth = 0;
            for (int i = 0; i < offset && i < s.Text.Length; i++)
            {
                if (s.States[i] != State.Code) continue;
                char c = s.Text[i];
                if (c == '(' || c == '[' || c == '{') depth++;
                else if ((c == ')' || c == ']' || c == '}') && s.Match[i] != -1) depth--;
            }
            return depth;
        }

        private static bool IsBlankLine(Scan s, int line)
        {
            for (int i = s.LineStarts[line]; i < s.LineEnd(line); i++)
            {
                if (!char.IsWhiteSpace(s.Text[i])) return false;
            }
            return true;
        }

        private static bool IsCommentLine(Scan s, int line)
        {
            int i = s.LineStarts[line];
            int end = s.LineEnd(line);
            while (i < end && char.IsWhiteSpace(s.Text[i])) i++;
            return i < end && (s.States[i] == State.LineComment || s.States[i] == State.BlockComment);
        }

        #endregion

        #region Edits

        private sealed class Edits
        {
            private readonly List<(int Start, int Length, string Text)> list = new List<(int, int, string)>();

            public void Replace(int start, int length, string text) => list.Add((start, length, text));

            public void Insert(int at, string text) => list.Add((at, 0, text));

            public string Apply(string text)
            {
                if (list.Count == 0) return text;
                StringBuilder sb = new StringBuilder();
                int pos = 0;
                foreach (var e in list.OrderBy(e => e.Start).ThenBy(e => e.Length))
                {
                    if (e.Start < pos) continue; // overlapping edits: the first one wins
                    sb.Append(text, pos, e.Start - pos);
                    sb.Append(e.Text);
                    pos = e.Start + e.Length;
                }
                sb.Append(text, pos, text.Length - pos);
                return sb.ToString();
            }
        }

        /// <summary>Code that can be changed: outside strings, comments and ignored regions.</summary>
        private static bool Editable(Scan s, int i) => i >= 0 && i < s.Text.Length && s.States[i] == State.Code && !s.Verbatim[i];

        #endregion

        #region Statements and blocks

        /// <summary>
        /// One statement per line and <c>} else {</c>: splits statements joined with ';', opens up one-line blocks, and
        /// moves else, elif, catch and finally up to the '}' before them.
        /// </summary>
        private static string Expand(string text, FormatOptions options)
        {
            Scan s = Read(text);
            Edits edits = new Edits();
            for (int i = 0; i < text.Length; i++)
            {
                if (!Editable(s, i)) continue;
                char c = text[i];

                if (options.StatementPerLine && c == '{' && s.IsBlock[i] && s.Match[i] > i)
                {
                    int close = s.Match[i];
                    int next = SkipSpaces(text, i + 1);
                    bool empty = next == close;
                    if (!empty && next < text.Length && text[next] != '\n' && s.States[next] == State.Code)
                    {
                        edits.Replace(i + 1, next - (i + 1), "\n");
                    }
                    if (!empty && Editable(s, close))
                    {
                        int before = close - 1;
                        while (before > i && (text[before] == ' ' || text[before] == '\t')) before--;
                        if (before > i && text[before] != '\n')
                        {
                            edits.Replace(before + 1, close - before - 1, "\n");
                        }
                    }
                }
                else if (options.StatementPerLine && c == ';' && !s.InExpression[i])
                {
                    int next = SkipSpaces(text, i + 1);
                    bool lineEnd = next >= text.Length || text[next] == '\n' || text[next] == '}' || s.States[next] == State.LineComment;
                    int prev = i - 1;
                    while (prev >= 0 && (text[prev] == ' ' || text[prev] == '\t')) prev--;
                    edits.Replace(prev + 1, next - (prev + 1), lineEnd ? (next < text.Length && s.States[next] == State.LineComment ? " " : string.Empty) : "\n");
                }
                else if (c == '}' && s.IsBlock[i])
                {
                    int next = SkipSpaces(text, i + 1);
                    // A word after a block's } on its line can only be else, catch and the like (the parser wants a
                    // new line before any other statement), so it stays where it is.
                    bool wordFollows = next < text.Length && text[next] != '\n' && s.States[next] == State.Code && Lexer.IsIdentStart(text[next]);
                    if (!wordFollows && options.BraceStyle)
                    {
                        // }\nelse { becomes } else {, when only whitespace is between them.
                        int p = i + 1;
                        while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
                        if (p > next && p < text.Length && Editable(s, p) && ChainWords.Contains(WordAt(text, p)) && !s.Verbatim[i + 1])
                        {
                            edits.Replace(i + 1, p - (i + 1), " ");
                        }
                    }
                }
            }
            return edits.Apply(text);
        }

        private static int SkipSpaces(string text, int i)
        {
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t' || text[i] == '\r')) i++;
            return i;
        }

        private static string WordAt(string text, int i)
        {
            int start = i;
            while (i < text.Length && Lexer.IsIdentChar(text[i])) i++;
            return text.Substring(start, i - start);
        }

        #endregion

        #region Spacing

        /// <summary>
        /// Spaces: after a comma, after the colon of <c>name: value</c> (attributes, named arguments, map keys,
        /// parameter types and type fields), around a statement's assignment and a named attribute's <c>=</c>, before a
        /// block's <c>{</c> and a trailing <c>//</c> comment, and after a block's <c>}</c>.
        /// </summary>
        private static string Space(string text)
        {
            Scan s = Read(text);
            Edits edits = new Edits();
            int statementStart = 0;
            bool assignedInStatement = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (s.States[i] == State.LineComment && (i == 0 || s.States[i - 1] != State.LineComment) && !s.Verbatim[i])
                {
                    char before = Peek(text, i - 1);
                    if (before != '\0' && before != ' ' && before != '\t' && before != '\n') edits.Insert(i, " ");
                }
                if (!Editable(s, i))
                {
                    continue;
                }
                if (c == '\n' && !s.InExpression[i] && !EndsWithContinuation(s, i) && !StartsWithContinuation(text, i + 1))
                {
                    statementStart = i + 1;
                    assignedInStatement = false;
                    continue;
                }
                if (c == '{' && s.IsBlock[i])
                {
                    statementStart = i + 1;
                    assignedInStatement = false;
                    char before = Peek(text, i - 1);
                    if (before != ' ' && before != '\t' && before != '\n') edits.Insert(i, " ");
                    continue;
                }
                if (c == '}' && s.IsBlock[i])
                {
                    statementStart = i + 1;
                    assignedInStatement = false;
                    if (Lexer.IsIdentStart(Peek(text, i + 1))) edits.Insert(i + 1, " ");
                    continue;
                }
                if (c == '=' && Peek(text, i + 1) == '>' && Peek(text, i - 1) != '=' && Peek(text, i - 1) != '>' && Peek(text, i - 1) != '<' && Peek(text, i - 1) != '!')
                {
                    // fn f(x) => expr, lambdas and match arms: one space either side of =>.
                    char before = Peek(text, i - 1);
                    if (before != ' ' && before != '\t' && before != '\n' && before != '\0') edits.Insert(i, " ");
                    char after = Peek(text, i + 2);
                    if (after != ' ' && after != '\t' && after != '\n' && after != '\0') edits.Insert(i + 2, " ");
                    i++;
                    continue;
                }
                if (c == '{' && !s.IsBlock[i] && s.Match[i] > i)
                {
                    // Map literals: { a: 1 } with a space inside the braces, when they open and close on one line.
                    int close = s.Match[i];
                    char next = Peek(text, i + 1);
                    if (close != i + 1 && text.IndexOf('\n', i, close - i) < 0 && Editable(s, close))
                    {
                        if (next != ' ' && next != '\t') edits.Insert(i + 1, " ");
                        char last = Peek(text, close - 1);
                        if (last != ' ' && last != '\t') edits.Insert(close, " ");
                    }
                    continue;
                }
                if (c == ',')
                {
                    int prev = i - 1;
                    while (prev >= 0 && (text[prev] == ' ' || text[prev] == '\t')) prev--;
                    if (prev < i - 1 && prev >= 0 && text[prev] != '\n' && text[prev] != ',' && text[prev] != '(' && text[prev] != '[') edits.Replace(prev + 1, i - prev - 1, string.Empty);
                    char next = Peek(text, i + 1);
                    if (next != ' ' && next != '\t' && next != '\n' && next != '\r' && next != ')' && next != ']' && next != '}' && next != ',' && next != '\0') edits.Insert(i + 1, " ");
                    continue;
                }
                if (c == ':' && Peek(text, i + 1) != ':' && Peek(text, i - 1) != ':' && AfterKey(s, i))
                {
                    char next = Peek(text, i + 1);
                    if (next != ' ' && next != '\t' && next != '\n' && next != '\r' && next != '\0' && next != '/') edits.Insert(i + 1, " ");
                    continue;
                }
                if (IsAssignment(text, i, out int length))
                {
                    bool statementLevel = !s.InExpression[i] && !assignedInStatement && LooksLikeTarget(s, statementStart, i);
                    bool named = s.InExpression[i] && length == 1 && AfterKey(s, i);
                    if (statementLevel || named)
                    {
                        char before = Peek(text, i - 1);
                        if (before != ' ' && before != '\t') edits.Insert(i, " ");
                        char after = Peek(text, i + length);
                        if (after != ' ' && after != '\t' && after != '\n' && after != '\0') edits.Insert(i + length, " ");
                    }
                    if (!s.InExpression[i]) assignedInStatement = true;
                    i += length - 1;
                }
            }
            return edits.Apply(text);
        }

        /// <summary>True at '=', '+=', '-=', '*=', '/=' or '??=' (not ==, =>, !=, &lt;= or &gt;=).</summary>
        private static bool IsAssignment(string text, int i, out int length)
        {
            length = 0;
            char c = text[i];
            if (c == '?' && Peek(text, i + 1) == '?' && Peek(text, i + 2) == '=' && Peek(text, i + 3) != '=')
            {
                length = 3;
                return true;
            }
            if ((c == '+' || c == '-' || c == '*' || c == '/') && Peek(text, i + 1) == '=' && Peek(text, i + 2) != '=' && Peek(text, i + 2) != '>')
            {
                char before = Peek(text, i - 1);
                if (before == c || before == '=' || before == '<' || before == '>' || before == '!') return false;
                length = 2;
                return true;
            }
            if (c == '=')
            {
                char before = Peek(text, i - 1), after = Peek(text, i + 1);
                if (after == '=' || after == '>' || before == '=' || before == '!' || before == '<' || before == '>' || before == '+'
                    || before == '-' || before == '*' || before == '/' || before == '?' || before == '%')
                {
                    return false;
                }
                length = 1;
                return true;
            }
            return false;
        }

        /// <summary>The statement so far is an assignment target: a name with optional .field, [index] or : type, maybe after const.</summary>
        private static bool LooksLikeTarget(Scan s, int start, int op)
        {
            string head = s.Text.Substring(start, op - start).Trim();
            if (head.StartsWith("const ", StringComparison.Ordinal)) head = head.Substring(6).TrimStart();
            if (head.Length == 0 || !(Lexer.IsIdentStart(head[0]) || head[0] == '$')) return false;
            string first = WordAt(head, head[0] == '$' ? 1 : 0);
            if (first is "if" or "elif" or "while" or "for" or "match" or "case" or "when" or "return" or "echo" or "throw" or "fn" or "import" or "type" or "enum" or "exit")
            {
                return false;
            }
            return true;
        }

        /// <summary>The character follows a name that follows '(', ',' or '{': an attribute, named argument, map key or field name.</summary>
        private static bool AfterKey(Scan s, int at)
        {
            string text = s.Text;
            int p = at - 1;
            while (p >= 0 && (text[p] == ' ' || text[p] == '\t')) p--;
            if (p < 0 || !(Lexer.IsIdentChar(text[p]) || text[p] == '?')) return false;
            if (text[p] == '?') p--;
            int end = p;
            while (p >= 0 && (Lexer.IsIdentChar(text[p]) || text[p] == '-')) p--;
            if (p == end) return false;
            if (p >= 0 && (text[p] == '$' || text[p] == '@' || text[p] == '.')) return false;
            while (p >= 0 && char.IsWhiteSpace(text[p])) p--;
            if (p < 0) return false;
            char c = text[p];
            if (!s.IsCode(p)) return false;
            if (c == '(' || c == ',') return s.InExpression[at];
            if (c == '{') return true;
            return false;
        }

        #endregion

        #region Layout

        /// <summary>
        /// Re-indents every line by nesting, trims trailing spaces, collapses blank lines and ends the file with one
        /// new line. Lines inside strings and ignored regions are copied; a /* */ comment's lines move with its first.
        /// </summary>
        private static string Layout(string text, FormatOptions options)
        {
            Scan s = Read(text);
            StringBuilder sb = new StringBuilder();
            List<(int Opener, int Line)> stack = new List<(int, int)>();
            int blankRun = 0;
            bool wroteLine = false;
            bool previousOpened = false;
            int commentDelta = 0;
            int lastCodeLine = -1;
            int lastColumns = 0;
            StringBlock block = null; // the ``` block whose lines are being copied, moved by blockDelta columns
            int blockDelta = 0;

            for (int line = 0; line < s.LineStarts.Count; line++)
            {
                int start = s.LineStarts[line];
                int end = s.LineEnd(line);
                string raw = text.Substring(start, end - start);
                bool startsInString = start < text.Length && start > 0 && (s.States[start - 1] == State.Single || s.States[start - 1] == State.Double) && (s.States[start] == State.Single || s.States[start] == State.Double);
                bool startsInComment = start > 0 && start < text.Length && s.States[start - 1] == State.BlockComment && s.States[start] == State.BlockComment && !IsCommentOpen(s, start);
                bool verbatim = start < text.Length && s.Verbatim[start];
                // After a \ that joins two lines of a string, the next line's indentation isn't part of the string.
                bool joined = startsInString && !verbatim && s.Joined[start - 1] && options.Indent;
                if (block != null && start >= block.End) block = null;

                // Closers at the start of the line.
                int first = start;
                while (first < end && (text[first] == ' ' || text[first] == '\t')) first++;
                bool blank = first >= end;

                if (joined)
                {
                    FlushBlanks(sb, ref blankRun, options);
                    string content = raw.TrimStart(' ', '\t');
                    bool endsInString = end > start && (s.States[end - 1] == State.Single || s.States[end - 1] == State.Double) && !ClosesString(s, end - 1);
                    if (options.TrimTrailingWhitespace && !endsInString) content = content.TrimEnd();
                    sb.Append(Indent(lastColumns + options.IndentSize, options)).Append(content).Append('\n');
                    wroteLine = true;
                    previousOpened = false;
                }
                else if (verbatim || startsInString)
                {
                    FlushBlanks(sb, ref blankRun, options);
                    sb.Append(block != null && !verbatim && start > block.Start ? Shift(raw, blockDelta, options) : raw).Append('\n');
                    wroteLine = true;
                    previousOpened = false;
                }
                else if (blank && !startsInComment)
                {
                    if (wroteLine) blankRun++;
                }
                else
                {
                    int columns;
                    if (startsInComment)
                    {
                        columns = Math.Max(0, Columns(raw, options) + commentDelta);
                    }
                    else
                    {
                        List<(int Opener, int Line)> effective = new List<(int, int)>(stack);
                        int p = first;
                        while (p < end && s.States[p] == State.Code && (text[p] == '}' || text[p] == ')' || text[p] == ']'))
                        {
                            if (s.Match[p] >= 0 && effective.Count > 0 && effective[effective.Count - 1].Opener == s.Match[p]) effective.RemoveAt(effective.Count - 1);
                            p++;
                            while (p < end && (text[p] == ' ' || text[p] == '\t')) p++;
                        }
                        int levels = effective.Select(e => e.Line).Distinct().Count();
                        bool inBlock = effective.Count == 0 || s.IsBlock[effective[effective.Count - 1].Opener];
                        bool closes = p > first;
                        if (inBlock && !closes && lastCodeLine >= 0 && IsContinuation(s, lastCodeLine, first))
                        {
                            levels++;
                        }
                        columns = options.Indent ? levels * options.IndentSize : Columns(raw, options);
                        if (IsCommentOpenLine(s, first, end))
                        {
                            commentDelta = columns - Columns(raw, options);
                        }
                    }

                    if (previousOpened && options.CollapseBlankLines) blankRun = 0;
                    if (options.CollapseBlankLines && !startsInComment && first < end && s.States[first] == State.Code && (text[first] == '}' || text[first] == ')' || text[first] == ']')) blankRun = 0;
                    FlushBlanks(sb, ref blankRun, options);

                    string content = raw.Substring(first - start);
                    bool endsInString = end > start && (s.States[end - 1] == State.Single || s.States[end - 1] == State.Double) && !(end - 1 >= 0 && ClosesString(s, end - 1));
                    if (options.TrimTrailingWhitespace && !endsInString) content = content.TrimEnd();
                    sb.Append(Indent(columns, options)).Append(content).Append('\n');
                    wroteLine = true;
                    if (!startsInComment) lastColumns = columns;
                    // A ``` block opened on this line moves with it: the closing fence sets the block's indentation.
                    block = s.Blocks.FirstOrDefault(b => b.Start >= start && b.Start < end && b.End > end);
                    blockDelta = block != null ? BlockDelta(text, block, columns - Columns(raw, options)) : 0;

                    if (!startsInComment || HasCode(s, start, end)) lastCodeLine = HasCode(s, start, end) ? line : lastCodeLine;
                    previousOpened = LastCodeChar(s, start, end) is int last && (text[last] == '{' || text[last] == '(' || text[last] == '[');
                }

                // Brackets opened and closed on this line, for the lines after it.
                for (int i = start; i < end; i++)
                {
                    if (s.States[i] != State.Code) continue;
                    char c = text[i];
                    if (c == '(' || c == '[' || c == '{')
                    {
                        stack.Add((i, line));
                    }
                    else if ((c == ')' || c == ']' || c == '}') && s.Match[i] >= 0)
                    {
                        int at = stack.FindLastIndex(e => e.Opener == s.Match[i]);
                        if (at >= 0) stack.RemoveRange(at, stack.Count - at);
                    }
                }
            }

            string result = sb.ToString();
            // The scan treats the text after the last '\n' as a line, so every line got a '\n': trim the end.
            int trimmed = result.Length;
            while (trimmed > 0 && (result[trimmed - 1] == '\n' || (options.TrimTrailingWhitespace && (result[trimmed - 1] == ' ' || result[trimmed - 1] == '\t')))) trimmed--;
            result = result.Substring(0, trimmed);
            if (result.Length == 0) return string.Empty;
            if (options.FinalNewline)
            {
                return result + "\n";
            }
            // Keep the file's own ending.
            int newlines = 0;
            for (int i = text.Length - 1; i >= 0 && char.IsWhiteSpace(text[i]); i--)
            {
                if (text[i] == '\n') newlines++;
            }
            return result + new string('\n', Math.Min(newlines, options.CollapseBlankLines ? options.MaxBlankLines + 1 : newlines));
        }

        /// <summary>
        /// How far a ``` block's lines can move with its first line: by <paramref name="delta"/> columns when its
        /// lines are indented with spaces deeply enough, else not at all.
        /// </summary>
        private static int BlockDelta(string text, StringBlock block, int delta)
        {
            if (delta == 0 || block.Error != null || block.ContentEnd >= text.Length) return 0;
            int fence = block.ContentEnd;
            while (fence < text.Length && text[fence] == ' ') fence++;
            if (text[fence] != '`' || (delta < 0 && fence - block.ContentEnd < -delta)) return 0;
            for (int i = block.ContentStart; i < block.ContentEnd;)
            {
                int lineEnd = text.IndexOf('\n', i);
                if (lineEnd < 0 || lineEnd > block.ContentEnd) lineEnd = block.ContentEnd;
                int spaces = 0;
                while (i + spaces < lineEnd && text[i + spaces] == ' ') spaces++;
                bool blank = string.IsNullOrWhiteSpace(text.Substring(i, lineEnd - i));
                if (!blank && (text[i + spaces] == '\t' || (delta < 0 && spaces < -delta))) return 0;
                i = lineEnd + 1;
            }
            return delta;
        }

        private static string Shift(string line, int delta, FormatOptions options)
        {
            if (delta == 0 || string.IsNullOrWhiteSpace(line)) return delta == 0 ? line : string.Empty;
            return delta > 0 ? new string(' ', delta) + line : line.Substring(-delta);
        }

        private static bool ClosesString(Scan s, int i) => i + 1 >= s.Text.Length || s.States[i + 1] == State.Code || s.States[i + 1] == State.LineComment || s.States[i + 1] == State.BlockComment;

        private static bool IsCommentOpen(Scan s, int i) => s.Text[i] == '/' && Peek(s.Text, i + 1) == '*' && (i == 0 || s.States[i - 1] != State.BlockComment || (i >= 2 && s.Text[i - 2] == '*' && s.Text[i - 1] == '/'));

        /// <summary>True when a /* comment that runs onto later lines starts on this line.</summary>
        private static bool IsCommentOpenLine(Scan s, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (s.States[i] == State.BlockComment && s.Text[i] == '/' && Peek(s.Text, i + 1) == '*' && (i == 0 || s.States[i - 1] != State.BlockComment))
                {
                    int close = s.Text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0 || close > end) return true;
                }
            }
            return false;
        }

        private static bool HasCode(Scan s, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                if (!char.IsWhiteSpace(s.Text[i]) && s.States[i] != State.LineComment && s.States[i] != State.BlockComment) return true;
            }
            return false;
        }

        private static int? LastCodeChar(Scan s, int start, int end)
        {
            for (int i = end - 1; i >= start; i--)
            {
                if (char.IsWhiteSpace(s.Text[i]) || s.States[i] == State.LineComment || s.States[i] == State.BlockComment) continue;
                return s.States[i] == State.Code ? i : null;
            }
            return null;
        }

        /// <summary>The line continues the statement on <paramref name="previousLine"/>: that line ends with an operator, or this one starts with one.</summary>
        private static bool IsContinuation(Scan s, int previousLine, int first)
        {
            int? last = LastCodeChar(s, s.LineStarts[previousLine], s.LineEnd(previousLine));
            if (last is int l && EndsWithContinuation(s, l + 1)) return true;
            return s.States[first] == State.Code && StartsWithContinuation(s.Text, first);
        }

        /// <summary>The code before <paramref name="end"/> (a line's end) finishes with an operator that carries the statement on.</summary>
        private static bool EndsWithContinuation(Scan s, int end)
        {
            int p = end - 1;
            while (p >= 0 && (char.IsWhiteSpace(s.Text[p]) && s.Text[p] != '\n' || s.States[p] == State.LineComment || s.States[p] == State.BlockComment)) p--;
            if (p < 0 || s.Text[p] == '\n' || s.States[p] != State.Code) return false;
            int lineStart = s.Text.LastIndexOf('\n', p) + 1;
            string line = s.Text.Substring(lineStart, p + 1 - lineStart);
            if (TrailingContinuations.Any(op => line.EndsWith(op, StringComparison.Ordinal)))
            {
                // A block's own { or a closing } never continue; '-' and '>' as part of -> and => are covered above.
                return true;
            }
            return WordContinuations.Any(w => line.EndsWith(" " + w, StringComparison.Ordinal));
        }

        private static bool StartsWithContinuation(string text, int p)
        {
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t')) p++;
            if (LeadingContinuations.Any(op => string.CompareOrdinal(text, p, op, 0, op.Length) == 0)) return true;
            foreach (string w in WordContinuations)
            {
                if (string.CompareOrdinal(text, p, w, 0, w.Length) == 0 && p + w.Length < text.Length && text[p + w.Length] == ' ') return true;
            }
            return false;
        }

        private static void FlushBlanks(StringBuilder sb, ref int blankRun, FormatOptions options)
        {
            int n = options.CollapseBlankLines ? Math.Min(blankRun, options.MaxBlankLines) : blankRun;
            for (int i = 0; i < n; i++) sb.Append('\n');
            blankRun = 0;
        }

        private static int Columns(string line, FormatOptions options)
        {
            int columns = 0;
            foreach (char c in line)
            {
                if (c == ' ') columns++;
                else if (c == '\t') columns += options.IndentSize - (columns % options.IndentSize);
                else break;
            }
            return columns;
        }

        private static string Indent(int columns, FormatOptions options)
        {
            if (columns <= 0) return string.Empty;
            if (!options.UseTabs) return new string(' ', columns);
            return new string('\t', columns / options.IndentSize) + new string(' ', columns % options.IndentSize);
        }

        #endregion

        #region Wrapping

        /// <summary>
        /// A line past the print width with a bracketed list on it (arguments, attributes, an array or a map, opened
        /// and closed on that line) gets one item per line. The outermost such list on the line is wrapped first.
        /// </summary>
        private static string Wrap(string text, FormatOptions options)
        {
            Scan s = Read(text);
            Edits edits = new Edits();
            for (int line = 0; line < s.LineStarts.Count; line++)
            {
                int start = s.LineStarts[line], end = s.LineEnd(line);
                if (start >= text.Length || s.Verbatim[start] || Columns(text.Substring(start, end - start), options) + (end - start) - LeadingWidth(text, start, end) <= options.PrintWidth)
                {
                    continue;
                }
                bool joined = start > 0 && s.Joined[start - 1];
                if (start > 0 && s.States[start - 1] != State.Code && s.States[start - 1] != State.LineComment && !joined) continue;

                bool wrapped = false;
                for (int i = start; i < end && options.WrapAttributes; i++)
                {
                    char c = text[i];
                    if (!Editable(s, i) || !(c == '(' || c == '[' || (c == '{' && !s.IsBlock[i]))) continue;
                    int close = s.Match[i];
                    if (close < 0 || close > end) continue;
                    // Only a list that itself runs past the width: wrapping (a, b) "long text" wouldn't help.
                    if (Columns(text.Substring(start, end - start), options) + (close + 1 - start) - LeadingWidth(text, start, end) <= options.PrintWidth) continue;
                    List<int> commas = TopLevelCommas(s, i, close);
                    if (commas.Count == 0 || commas.Any(k => !Editable(s, k))) continue;

                    edits.Replace(i + 1, SkipAllSpaces(text, i + 1) - (i + 1), "\n");
                    foreach (int k in commas)
                    {
                        edits.Replace(k + 1, SkipAllSpaces(text, k + 1) - (k + 1), "\n");
                    }
                    int before = close - 1;
                    while (before > i && (text[before] == ' ' || text[before] == '\t')) before--;
                    edits.Replace(before + 1, close - before - 1, "\n");
                    wrapped = true;
                    break;
                }
                if (!wrapped && options.WrapStrings)
                {
                    WrapString(s, start, end, options, edits);
                }
            }
            return edits.Apply(text);
        }

        /// <summary>
        /// A string that runs past the print width is broken at the last space that fits, with a \ at the end of the
        /// line: the break and the next line's indentation aren't part of the string. Text inside { } in "text" is
        /// never split, and the rest of the line moves down with the string.
        /// </summary>
        private static void WrapString(Scan s, int start, int end, FormatOptions options, Edits edits)
        {
            string text = s.Text;
            int leadingChars = LeadingWidth(text, start, end);
            int leadingColumns = Columns(text.Substring(start, end - start), options);
            int Column(int i) => leadingColumns + (i - start) - leadingChars;

            int braces = 0;
            int best = -1;
            bool inBrackets = false;
            if (start > 0 && s.Joined[start - 1])
            {
                int q = start - 1;
                while (q > 0 && !IsQuote(s, q)) q--;
                inBrackets = s.InExpression[q];
            }
            for (int i = start + leadingChars; i < end; i++)
            {
                State state = s.States[i];
                if (state != State.Single && state != State.Double)
                {
                    braces = 0;
                    continue;
                }
                // Only a string that is a statement's text or value: one inside ( [ { is left for the list to wrap.
                if (IsQuote(s, i)) inBrackets = s.InExpression[i];
                if (s.Verbatim[i] || inBrackets || IsInBlock(s, i)) continue;
                char c = text[i];
                if (c == '\\' && i + 1 < end)
                {
                    i++;
                    continue;
                }
                if (state == State.Double && c == '{') braces++;
                else if (state == State.Double && c == '}' && braces > 0) braces--;
                // Break after a space that is inside the string, followed by more of the string (not its closing quote).
                if (c != ' ' || braces > 0 || i + 1 >= end || text[i + 1] == ' ' || text[i + 1] == '\\' || s.States[i + 1] != state || ClosesString(s, i + 1) || IsQuote(s, i)) continue;
                if (i == start + leadingChars || s.States[i - 1] != state || IsQuote(s, i - 1)) continue;
                // Only a break that makes the line fit: if the string starts past the width, wrapping it won't help.
                if (Column(i + 1) + 1 > options.PrintWidth) break;
                best = i;
            }
            if (best < 0) return;
            int indent = (start > 0 && s.Joined[start - 1] ? leadingColumns : leadingColumns + options.IndentSize);
            if (Column(best + 1) <= indent + 1) return; // breaking here wouldn't make the line any shorter
            edits.Replace(best + 1, 0, "\\\n" + Indent(indent, options));
        }

        private static bool IsQuote(Scan s, int i) => (s.Text[i] == '"' || s.Text[i] == '\'') && (i == 0 || s.States[i - 1] == State.Code || s.States[i - 1] == State.LineComment || s.States[i - 1] == State.BlockComment);

        private static bool IsInBlock(Scan s, int i) => s.Blocks.Any(b => i >= b.Start && i < b.End);

        private static int LeadingWidth(string text, int start, int end)
        {
            int i = start;
            while (i < end && (text[i] == ' ' || text[i] == '\t')) i++;
            return i - start;
        }

        private static int SkipAllSpaces(string text, int i)
        {
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t')) i++;
            return i;
        }

        private static List<int> TopLevelCommas(Scan s, int open, int close)
        {
            List<int> commas = new List<int>();
            for (int i = open + 1; i < close; i++)
            {
                if (s.States[i] != State.Code) continue;
                char c = s.Text[i];
                if ((c == '(' || c == '[' || c == '{') && s.Match[i] > i)
                {
                    i = s.Match[i];
                    continue;
                }
                if (c == ',') commas.Add(i);
            }
            return commas;
        }

        #endregion

        private static string DominantEndOfLine(string text)
        {
            int crlf = 0, lf = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    if (i > 0 && text[i - 1] == '\r') crlf++;
                    else lf++;
                }
            }
            return crlf > lf ? "\r\n" : "\n";
        }
    }
}
