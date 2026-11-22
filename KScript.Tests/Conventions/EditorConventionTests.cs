using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using KScript.Syntax;

namespace KScript.Tests.Conventions;

/// <summary>
/// The .kscript editor grammar colours only what the language has: its operators and word operators are compared
/// with the lexer's own lists, so the editor can't present <c>is</c>, <c>++</c> or <c>%=</c> as valid again, and
/// its strings escape only what the language escapes.
/// </summary>
public sealed class EditorConventionTests
{
    private static JsonElement Grammar()
    {
        string path = Path.Combine(CliConventionTests.RepoRoot(), "editors", "vscode", "syntaxes", "kscript-code.tmLanguage.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Patterns(string rule) =>
        Grammar().GetProperty("repository").GetProperty(rule).GetProperty("patterns").EnumerateArray();

    private static string[] LexerOperators() =>
        (string[])typeof(Lexer).GetField("Operators", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    /// <summary>The texts a simple grammar pattern matches: alternatives, an optional last character (x=?) and one [class].</summary>
    internal static List<string> Expand(string pattern)
    {
        var result = new List<string>();
        foreach (string alternative in Regex.Split(pattern, @"(?<!\\)\|"))
        {
            Match set = Regex.Match(alternative, @"^\[(.+)\]$");
            if (set.Success)
            {
                result.AddRange(set.Groups[1].Value.Replace("\\", string.Empty).Select(c => c.ToString()));
                continue;
            }
            Match optional = Regex.Match(alternative, @"^(.*)(\\?.)\?$");
            if (optional.Success)
            {
                result.Add(Unescape(optional.Groups[1].Value));
                result.Add(Unescape(optional.Groups[1].Value + optional.Groups[2].Value));
                continue;
            }
            result.Add(Unescape(alternative));
        }
        return result;
    }

    private static string Unescape(string text) => Regex.Replace(text, @"\\(.)", "$1");

    [Fact]
    public void Expanding_a_pattern_gives_each_text_it_matches()
    {
        Assert.Equal(new[] { "->", "|>", "?", "?=" }, Expand(@"->|\|>|\?=?"));
        Assert.Equal(new[] { "??", "??=", "**", "-", "+", "%" }, Expand(@"\?\?=?|\*\*|[-+%]"));
    }

    [Fact]
    public void Every_operator_the_grammar_colours_is_one_the_lexer_has()
    {
        string[] lexer = LexerOperators();
        var coloured = Patterns("operators")
            .Where(p => p.GetProperty("name").GetString() is "keyword.operator.kscript" or "storage.type.function.arrow.kscript")
            .SelectMany(p => Expand(p.GetProperty("match").GetString())).Distinct().ToList();
        Assert.True(coloured.Count >= 20, "The grammar's operator patterns were not found.");
        var unknown = coloured.Where(op => !lexer.Contains(op)).ToList();
        Assert.True(unknown.Count == 0, "The grammar colours operators the language doesn't have: " + string.Join(" ", unknown));

        // The other way round: every operator of more than one character that the parser accepts is coloured.
        // && and || are read only to say "use and / or", so they are not.
        var notColoured = lexer.Where(op => op.Length > 1 && op is not ("&&" or "||")).Where(op => !coloured.Contains(op)).ToList();
        Assert.True(notColoured.Count == 0, "The grammar doesn't colour these operators: " + string.Join(" ", notColoured));
        foreach (string gone in new[] { "++", "--", "%=", "&&", "||" }) Assert.DoesNotContain(gone, coloured);
    }

    /// <summary>The comparison words of an XML script, where a raw &lt; can't be written. A .kscript file uses the symbols.</summary>
    private static readonly string[] XmlSpellings = { "eq", "ne", "lt", "le", "gt", "ge" };

    [Fact]
    public void Every_word_operator_and_constant_the_grammar_colours_is_a_keyword_of_the_lexer()
    {
        string[] operators = Patterns("operators").Where(p => p.GetProperty("name").GetString() == "keyword.operator.word.kscript")
            .SelectMany(p => Regex.Match(p.GetProperty("match").GetString(), @"\(((?:[a-z]+\|)*[a-z]+)\)").Groups[1].Value.Split('|')).ToArray();
        // Logic and "in" are words; comparisons are symbols in a .kscript file (eq, lt and the rest are the XML
        // spellings, which KS6012 reports there). "is" is the word of a match arm (case is ...), read by the code parser.
        Assert.Equal(new[] { "and", "in", "is", "not", "or", "step" }, operators.OrderBy(w => w, StringComparer.Ordinal));
        Assert.All(operators.Where(w => w != "is"), w => Assert.Contains(w, Lexer.Keywords));

        string constants = Patterns("constants").Single(p => p.GetProperty("name").GetString() == "constant.language.kscript").GetProperty("match").GetString();
        string[] values = Regex.Match(constants, @"\(((?:[a-z]+\|)+[a-z]+)\)").Groups[1].Value.Split('|');
        Assert.Equal(new[] { "false", "null", "true" }, values.OrderBy(w => w, StringComparer.Ordinal));
        Assert.All(values, w => Assert.Contains(w, Lexer.Keywords));

        // Every keyword of the lexer is coloured somewhere in the grammar.
        string all = Grammar().GetRawText();
        Assert.All(Lexer.Keywords.Where(k => !XmlSpellings.Contains(k)), k => Assert.Matches(@"[(|]" + k + @"[|)]", all));
        Assert.All(XmlSpellings, k => Assert.Contains(k, Lexer.Keywords));
    }

    [Fact]
    public void Strings_escape_only_what_the_language_escapes()
    {
        JsonElement repository = Grammar().GetProperty("repository");
        static string[] Escapes(JsonElement rule) => rule.GetProperty("patterns").EnumerateArray()
            .Where(p => p.TryGetProperty("name", out JsonElement n) && n.GetString() == "constant.character.escape.kscript")
            .Select(p => p.GetProperty("match").GetString()).ToArray();

        // 'Single quotes' keep text as written; "double quotes" escape only \" and \\ (Quotes.IsEscape).
        Assert.Empty(Escapes(repository.GetProperty("string")));
        string[] inDouble = Escapes(repository.GetProperty("text"));
        Assert.Equal(new[] { @"\\[\""\\]" }, inDouble);
        var escape = new Regex(inDouble[0]);
        foreach (char c in "\"\\nt'$ {0")
        {
            string text = "\"a\\" + c + "\"";
            Assert.Equal(KScript.Syntax.Text.Quotes.IsEscape(text, 2, text.Length, '"'), escape.IsMatch("\\" + c));
            Assert.False(KScript.Syntax.Text.Quotes.IsEscape("'a\\" + c + "'", 2, 5, '\''));
        }
    }
}
