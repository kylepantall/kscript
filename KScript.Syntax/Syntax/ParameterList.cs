using System;
using System.Collections.Generic;
using System.Linq;
using KScript.Syntax.Text;

namespace KScript.Syntax
{
    /// <summary>One parameter as written in a parameter list. Offsets are into the list's text.</summary>
    public sealed class ParameterEntry
    {
        public string Name { get; internal set; }
        public int NameStart { get; internal set; }

        /// <summary>The type as written, or null.</summary>
        public string Type { get; internal set; }
        public int TypeStart { get; internal set; } = -1;

        /// <summary>The default as written, or null.</summary>
        public string Default { get; internal set; }
        public int DefaultStart { get; internal set; } = -1;

        /// <summary>A <c>?</c> after the name outside a group (<c>recursive?: bool</c>, used by catalog signatures).</summary>
        public bool Optional { get; internal set; }

        /// <summary>The whole entry: <c>count: int = 1</c>, or the whole group for a group member.</summary>
        public int Start { get; internal set; }
        public int Length { get; internal set; }

        /// <summary>
        /// Which one-of group the parameter is in (0, 1, ... in order), or -1. A caller passes exactly one member of a
        /// group, by name, or at most one when <see cref="GroupOptional"/>.
        /// </summary>
        public int Group { get; internal set; } = -1;

        public bool GroupOptional { get; internal set; }

        /// <summary>True when the group's members share one type (<c>add|remove: file</c>).</summary>
        public bool GroupSharesType { get; internal set; }

        public bool InGroup => Group >= 0;
    }

    /// <summary>A mistake in a parameter list: where it is (into the list's text) and what to say.</summary>
    public sealed record ParameterProblem(int Start, int Length, string Message);

    /// <summary>
    /// The one reader of parameter lists, shared by the interpreter, the editor and the catalog:
    /// <c>who, count: int = 1</c>, and one-of groups, where a caller passes exactly one of the names:
    /// <c>add|remove: file</c> (one type), <c>text: string | template: Template</c> (a type each),
    /// <c>at|after?: date</c> (at most one), and brackets around any group: <c>(text: string | template: Template)?</c>.
    /// A group whose members have different types needs brackets when other parameters sit beside it.
    /// </summary>
    public static class ParameterList
    {
        public static List<ParameterEntry> Read(string text, List<ParameterProblem> problems = null)
        {
            var entries = new List<ParameterEntry>();
            if (string.IsNullOrWhiteSpace(text)) return entries;
            List<(int Start, int End)> parts = Split(text, 0, text.Length, ',');
            int nonEmpty = parts.Count(p => text.Substring(p.Start, p.End - p.Start).Trim().Length > 0);
            int groups = 0;
            foreach ((int start, int end) in parts)
            {
                (int s, int e) = Trim(text, start, end);
                if (s >= e) continue;
                if (text[s] == '(')
                {
                    int close = Matching(text, s, e);
                    if (close < 0)
                    {
                        problems?.Add(new ParameterProblem(s, e - s, "This group has no closing bracket: write (text: string | template: Template)."));
                        continue;
                    }
                    (int after, int afterEnd) = Trim(text, close + 1, e);
                    bool optional = after < afterEnd && text[after] == '?';
                    if (optional) after++;
                    if (after < afterEnd)
                    {
                        problems?.Add(new ParameterProblem(after, afterEnd - after, "Nothing can follow a group's brackets except ? (at most one of them). A group's members have no defaults."));
                        continue;
                    }
                    ReadGroup(text, s + 1, close, s, e - s, optional, bracketed: true, alone: true, groups++, entries, problems);
                    continue;
                }
                int bar = IndexOf(text, s, e, '|', stopAt: '=');
                int colon = IndexOf(text, s, e, ':');
                if (bar >= 0 && (colon < 0 || bar < colon || HasMemberAfter(text, bar, e)))
                {
                    ReadGroup(text, s, e, s, e - s, optional: false, bracketed: false, alone: nonEmpty == 1, groups++, entries, problems);
                    continue;
                }
                entries.Add(ReadOne(text, s, e, s, e - s));
            }
            return entries;
        }

        /// <summary>The names of a group, as a caller would write them: <c>add: or remove:</c>, <c>post:, put: or patch:</c>.</summary>
        public static string Choices(IReadOnlyList<string> names) =>
            names.Count <= 1 ? (names.Count == 1 ? names[0] + ":" : string.Empty)
            : string.Join(", ", names.Take(names.Count - 1).Select(n => n + ":")) + " or " + names[names.Count - 1] + ":";

        /// <summary>
        /// The one wording for a call that passed <paramref name="passed"/> members of a group: null when that is fine,
        /// otherwise "@file() takes add: or remove:, not both." or "@file() needs add: or remove:.".
        /// </summary>
        public static string GroupProblem(string display, IReadOnlyList<string> names, bool optional, int passed)
        {
            if (passed > 1)
                return names.Count == 2 ? $"{display} takes {Choices(names)}, not both." : $"{display} takes one of {Choices(names)}, not more than one.";
            if (passed == 0 && !optional)
                return names.Count == 2 ? $"{display} needs {Choices(names)}." : $"{display} needs one of {Choices(names)}.";
            return null;
        }

        /// <summary>
        /// The signature text for a list: a group is written as it is declared, <c>add|remove: file</c> for one type and
        /// <c>(text: string | template: Template)</c> for a type each, with <c>?</c> when it is optional.
        /// </summary>
        public static string Label<T>(IReadOnlyList<T> parameters, Func<T, int> group, Func<T, bool> groupOptional, Func<T, string> name, Func<T, string> type, Func<T, string> single)
        {
            var parts = new List<string>();
            for (int i = 0; i < parameters.Count; i++)
            {
                T p = parameters[i];
                int g = group(p);
                if (g < 0)
                {
                    parts.Add(single(p));
                    continue;
                }
                var members = new List<T> { p };
                while (i + 1 < parameters.Count && group(parameters[i + 1]) == g) members.Add(parameters[++i]);
                parts.Add(GroupLabel(members.Select(name).ToList(), members.Select(type).ToList(), groupOptional(p)));
            }
            return string.Join(", ", parts);
        }

        /// <summary><c>add|remove: file</c>, <c>at|after?: date</c>, or <c>(text: string | template: Template)?</c>.</summary>
        public static string GroupLabel(IReadOnlyList<string> names, IReadOnlyList<string> types, bool optional)
        {
            if (types.Distinct(StringComparer.Ordinal).Count() == 1)
                return string.Join("|", names) + (optional ? "?" : string.Empty) + (types[0] != null ? ": " + types[0] : string.Empty);
            return "(" + string.Join(" | ", names.Select((n, i) => n + (types[i] != null ? ": " + types[i] : string.Empty))) + ")" + (optional ? "?" : string.Empty);
        }

        private static void ReadGroup(string text, int start, int end, int whole, int wholeLength, bool optional, bool bracketed, bool alone, int group,
            List<ParameterEntry> entries, List<ParameterProblem> problems)
        {
            var members = new List<ParameterEntry>();
            int eq = IndexOf(text, start, end, '=');
            if (eq >= 0)
            {
                problems?.Add(new ParameterProblem(eq, end - eq, "A member of a one-of group can't have a default: a caller passes one of them, and the others are null."));
                end = eq;
            }
            int bar = IndexOf(text, start, end, '|');
            int colon = IndexOf(text, start, end, ':');
            bool shared = colon < 0 || (bar < colon && IndexOf(text, colon, end, '|') < 0);
            if (shared)
            {
                // add|remove: file, at|after?: date
                int namesEnd = colon >= 0 ? colon : end;
                string type = null;
                int typeStart = -1;
                if (colon >= 0)
                {
                    (int ts, int te) = Trim(text, colon + 1, end);
                    type = text.Substring(ts, te - ts);
                    typeStart = ts;
                }
                List<(int Start, int End)> names = Split(text, start, namesEnd, '|');
                for (int i = 0; i < names.Count; i++)
                {
                    (int ns, int ne) = Trim(text, names[i].Start, names[i].End);
                    bool marked = ne > ns && text[ne - 1] == '?';
                    if (marked)
                    {
                        ne--;
                        if (bracketed) problems?.Add(new ParameterProblem(ne, 1, "Put the ? after the brackets: (add|remove: file)?."));
                        else if (i < names.Count - 1) problems?.Add(new ParameterProblem(ne, 1, "Put the ? once, after the last name: at|after?: date."));
                        else optional = true;
                    }
                    members.Add(new ParameterEntry { Name = text.Substring(ns, ne - ns), NameStart = ns, Type = type, TypeStart = typeStart, GroupSharesType = true });
                }
            }
            else
            {
                // text: string | template: Template
                List<(int Start, int End)> parts = Split(text, start, end, '|');
                int marks = 0;
                foreach ((int ps, int pe) in parts)
                {
                    (int s, int e) = Trim(text, ps, pe);
                    ParameterEntry member = ReadOne(text, s, e, whole, wholeLength);
                    if (member.Optional)
                    {
                        marks++;
                        if (bracketed) problems?.Add(new ParameterProblem(member.NameStart + member.Name.Length, 1, "Put the ? after the brackets: (text: string | template: Template)?."));
                    }
                    members.Add(member);
                }
                if (!bracketed && marks > 0)
                {
                    if (marks == members.Count) optional = true;
                    else problems?.Add(new ParameterProblem(whole, wholeLength, "Mark every member of the group with ?, or none of them."));
                }
                if (!bracketed && !alone)
                    problems?.Add(new ParameterProblem(whole, wholeLength,
                        $"Put brackets around a group whose members have different types when other parameters sit beside it: {GroupLabel(members.Select(m => m.Name).ToList(), members.Select(m => m.Type).ToList(), optional)}."));
            }
            if (members.Count < 2)
                problems?.Add(new ParameterProblem(whole, wholeLength, "A one-of group needs two or more names: add|remove: file."));
            foreach (ParameterEntry m in members)
            {
                m.Optional = false;
                m.Group = group;
                m.GroupOptional = optional;
                m.GroupSharesType = shared;
                m.Start = whole;
                m.Length = wholeLength;
                entries.Add(m);
            }
        }

        /// <summary><c>name</c>, <c>name?</c>, <c>name: type</c>, <c>name = default</c>, <c>name: type = default</c>.</summary>
        private static ParameterEntry ReadOne(string text, int s, int e, int whole, int wholeLength)
        {
            var entry = new ParameterEntry { Start = whole, Length = wholeLength };
            int eq = IndexOf(text, s, e, '=');
            int headEnd = eq >= 0 ? eq : e;
            if (eq >= 0)
            {
                (int ds, int de) = Trim(text, eq + 1, e);
                entry.Default = text.Substring(ds, de - ds);
                entry.DefaultStart = ds;
            }
            int colon = IndexOf(text, s, headEnd, ':');
            int nameEnd = colon >= 0 ? colon : headEnd;
            if (colon >= 0)
            {
                (int ts, int te) = Trim(text, colon + 1, headEnd);
                entry.Type = text.Substring(ts, te - ts);
                entry.TypeStart = ts;
            }
            (int ns, int ne) = Trim(text, s, nameEnd);
            if (ne > ns && text[ne - 1] == '?')
            {
                entry.Optional = true;
                (ns, ne) = Trim(text, ns, ne - 1);
            }
            entry.Name = text.Substring(ns, ne - ns);
            entry.NameStart = ns;
            return entry;
        }

        /// <summary>True when a <c>name:</c> follows the bar: <c>text: string | template: Template</c>, not <c>mode: a|b</c>.</summary>
        private static bool HasMemberAfter(string text, int bar, int end)
        {
            int colon = IndexOf(text, bar + 1, end, ':');
            if (colon < 0) return false;
            string name = text.Substring(bar + 1, colon - bar - 1).Trim().TrimEnd('?').Trim();
            return name.Length > 0 && Lexer.IsIdentStart(name[0]) && name.All(Lexer.IsIdentChar);
        }

        /// <summary>Where the bars of a group are: the top-level <c>|</c> between <paramref name="start"/> and <paramref name="end"/>.</summary>
        public static IEnumerable<int> Bars(string text, int start, int end)
        {
            int from = start;
            while (from < end && IndexOf(text, from, end, '|') is int bar && bar >= 0)
            {
                // Inside brackets, the group's own bars are one level down.
                yield return bar;
                from = bar + 1;
            }
            if (start < end && text[start] == '(' && Matching(text, start, end) is int close && close > start)
                foreach (int bar in Bars(text, start + 1, close)) yield return bar;
        }

        private static (int, int) Trim(string text, int start, int end)
        {
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            return (start, end);
        }

        /// <summary>The top-level pieces between separators, outside brackets and quotes.</summary>
        private static List<(int Start, int End)> Split(string text, int start, int end, char separator)
        {
            var parts = new List<(int, int)>();
            int depth = 0, from = start;
            char quote = '\0';
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (quote != '\0') { if (Quotes.IsEscape(text, i, end, quote)) i++; else if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') quote = c;
                else if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == separator && depth == 0)
                {
                    parts.Add((from, i));
                    from = i + 1;
                }
            }
            parts.Add((from, end));
            return parts;
        }

        /// <summary>The first top-level <paramref name="target"/>, before any top-level <paramref name="stopAt"/>, or -1.</summary>
        private static int IndexOf(string text, int start, int end, char target, char stopAt = '\0')
        {
            int depth = 0;
            char quote = '\0';
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (quote != '\0') { if (Quotes.IsEscape(text, i, end, quote)) i++; else if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') quote = c;
                else if (c == '(' || c == '[' || c == '{' || c == '<') depth++;
                else if (c == ')' || c == ']' || c == '}' || c == '>') depth--;
                else if (depth == 0 && c == stopAt) return -1;
                else if (depth == 0 && c == target) return i;
            }
            return -1;
        }

        private static int Matching(string text, int open, int end)
        {
            int depth = 0;
            char quote = '\0';
            for (int i = open; i < end; i++)
            {
                char c = text[i];
                if (quote != '\0') { if (Quotes.IsEscape(text, i, end, quote)) i++; else if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') quote = c;
                else if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
