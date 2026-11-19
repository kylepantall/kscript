using System.Collections.Generic;
using System.Text;

using KScript.KScriptExceptions;

namespace KScript.CommandHandler
{
    /// <summary>
    /// A piece of a string containing KScript commands: either literal text or a command call.
    /// </summary>
    public abstract class CommandSegment { }

    /// <summary>Literal text, copied to the output unchanged.</summary>
    public sealed class TextSegment : CommandSegment
    {
        public TextSegment(string text) => Text = text;
        public string Text { get; }
        public override string ToString() => Text;
    }

    /// <summary>
    /// A command call such as <c>@to_lower('HELLO')</c>.
    /// </summary>
    public sealed class CommandCall : CommandSegment
    {
        public CommandCall(string name, List<CommandArgument> arguments, string source)
        {
            Name = name;
            Arguments = arguments;
            Source = source;
        }

        /// <summary>The command name without the leading '@'.</summary>
        public string Name { get; }

        public List<CommandArgument> Arguments { get; }

        /// <summary>The original text of the call, e.g. <c>@to_lower('HELLO')</c>.</summary>
        public string Source { get; }

        public override string ToString() => Source;
    }

    /// <summary>
    /// One argument of a command call. An argument is a sequence of parts that are concatenated:
    /// quoted strings, nested command calls and unquoted text.
    /// </summary>
    public sealed class CommandArgument
    {
        public List<ArgumentPart> Parts { get; } = new List<ArgumentPart>();

        /// <summary>True when the argument contains nothing but whitespace.</summary>
        public bool IsEmpty
        {
            get
            {
                foreach (ArgumentPart part in Parts)
                {
                    if (part.Kind != ArgumentPartKind.Raw || !string.IsNullOrWhiteSpace(part.Text))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }

    public enum ArgumentPartKind
    {
        /// <summary>Unquoted text. Leading/trailing whitespace of the argument is trimmed.</summary>
        Raw,

        /// <summary>Text inside single quotes, kept verbatim.</summary>
        Quoted,

        /// <summary>A nested command call.</summary>
        Command
    }

    public sealed class ArgumentPart
    {
        private ArgumentPart(ArgumentPartKind kind, string text, CommandCall command)
        {
            Kind = kind;
            Text = text;
            Command = command;
        }

        public ArgumentPartKind Kind { get; }
        public string Text { get; }
        public CommandCall Command { get; }

        public static ArgumentPart Raw(string text) => new ArgumentPart(ArgumentPartKind.Raw, text, null);
        public static ArgumentPart Quoted(string text) => new ArgumentPart(ArgumentPartKind.Quoted, text, null);
        public static ArgumentPart Call(CommandCall call) => new ArgumentPart(ArgumentPartKind.Command, null, call);
    }

    /// <summary>
    /// Recursive-descent parser for KScript command expressions.
    ///
    /// Grammar:
    /// <code>
    ///   text      := ( command | any-char )*
    ///   command   := '@' identifier '(' arguments? ')'
    ///   arguments := argument ( ',' argument )*
    ///   argument  := ( quoted | command | '(' raw-balanced ')' | raw-char )*
    ///   quoted    := "'" any-char-except-quote* "'"
    /// </code>
    ///
    /// An '@' only starts a command when it is followed by an identifier and an opening bracket,
    /// so text such as e-mail addresses is left alone. Quotes outside of a command are plain text.
    /// </summary>
    public static class CommandExpressionParser
    {
        private const char Quote = '\'';

        /// <summary>
        /// Returns true if the string contains at least one well-formed command call.
        /// </summary>
        public static bool ContainsCommand(string input)
        {
            if (string.IsNullOrEmpty(input) || input.IndexOf('@') < 0)
            {
                return false;
            }

            for (int i = 0; i < input.Length; i++)
            {
                if (IsCommandStart(input, i, out _))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Splits the input into literal text and command calls.
        /// </summary>
        /// <exception cref="KScriptException">If a command call is malformed (unterminated string or missing bracket).</exception>
        public static List<CommandSegment> Parse(string input)
        {
            List<CommandSegment> segments = new List<CommandSegment>();

            if (string.IsNullOrEmpty(input))
            {
                return segments;
            }

            StringBuilder text = new StringBuilder();
            int i = 0;

            while (i < input.Length)
            {
                if (IsCommandStart(input, i, out _))
                {
                    if (text.Length > 0)
                    {
                        segments.Add(new TextSegment(text.ToString()));
                        text.Clear();
                    }

                    segments.Add(ParseCommand(input, ref i));
                    continue;
                }

                text.Append(input[i]);
                i++;
            }

            if (text.Length > 0)
            {
                segments.Add(new TextSegment(text.ToString()));
            }

            return segments;
        }

        /// <summary>
        /// Determines whether a command starts at <paramref name="index"/>: '@', an identifier, then '('.
        /// </summary>
        private static bool IsCommandStart(string input, int index, out int openBracketIndex)
        {
            openBracketIndex = -1;

            if (input[index] != '@' || index + 1 >= input.Length || !IsIdentifierStart(input[index + 1]))
            {
                return false;
            }

            int j = index + 2;
            while (j < input.Length && IsIdentifierPart(input[j]))
            {
                j++;
            }

            if (j < input.Length && input[j] == '(')
            {
                openBracketIndex = j;
                return true;
            }

            return false;
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static CommandCall ParseCommand(string input, ref int i)
        {
            int start = i;
            IsCommandStart(input, i, out int open);
            string name = input.Substring(start + 1, open - start - 1);
            i = open + 1;

            List<CommandArgument> arguments = new List<CommandArgument>();
            CommandArgument current = new CommandArgument();
            StringBuilder raw = new StringBuilder();
            int depth = 0;

            void FlushRaw()
            {
                if (raw.Length > 0)
                {
                    current.Parts.Add(ArgumentPart.Raw(raw.ToString()));
                    raw.Clear();
                }
            }

            while (i < input.Length)
            {
                char c = input[i];

                if (c == Quote)
                {
                    int close = input.IndexOf(Quote, i + 1);
                    if (close < 0)
                    {
                        throw new KScriptException(
                            $"Unterminated string in command '@{name}': a closing (') is missing.\n" +
                            "Error occurs when parsing string:\n----------------------------------------------------\n" + input);
                    }

                    FlushRaw();
                    current.Parts.Add(ArgumentPart.Quoted(input.Substring(i + 1, close - i - 1)));
                    i = close + 1;
                    continue;
                }

                if (c == '@' && IsCommandStart(input, i, out _))
                {
                    FlushRaw();
                    current.Parts.Add(ArgumentPart.Call(ParseCommand(input, ref i)));
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth == 0)
                    {
                        FlushRaw();
                        arguments.Add(current);
                        i++;

                        // "@cmd()" has no arguments rather than one empty argument.
                        if (arguments.Count == 1 && arguments[0].IsEmpty)
                        {
                            arguments.Clear();
                        }

                        return new CommandCall(name, arguments, input.Substring(start, i - start));
                    }
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    FlushRaw();
                    arguments.Add(current);
                    current = new CommandArgument();
                    i++;
                    continue;
                }

                raw.Append(c);
                i++;
            }

            throw new KScriptException(
                $"Command '@{name}' is missing a closing bracket ')'.\n" +
                "Error occurs when parsing string:\n----------------------------------------------------\n" + input);
        }
    }
}
