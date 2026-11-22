using System.Text.RegularExpressions;

using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Tests.Conventions;

/// <summary>
/// How the standard packages behave, checked over everything every package registers, so one package can't drift
/// from the rest: names that answer true or false, fields and methods, the verbs for keyed things, lengths of
/// time, and the arguments a table takes. The rules are written out in docs/contributing.md ("How the standard
/// packages behave"). Each allow-list is short and says why each name is on it.
/// </summary>
public class StdlibBehaviourConventionTests
{
    private static readonly LanguageCatalog Catalog = LanguageCatalog.Default;

    private static IEnumerable<CallableInfo> Callables =>
        Catalog.Commands.Concat(Catalog.Methods).Where(c => !c.Planned && c.Kind != CallableKind.Function);

    private static IEnumerable<ElementInfo> Elements => Catalog.Elements.Where(e => !e.Planned);

    private static string Show(CallableInfo c) => c.Kind == CallableKind.Method ? $"{c.ReceiverType}->{c.Name}()" : $"@{c.Name}()";

    private static void None(IEnumerable<string> broken, string rule)
    {
        List<string> list = broken.Distinct().OrderBy(b => b, StringComparer.Ordinal).ToList();
        Assert.True(list.Count == 0, rule + "\n  " + string.Join("\n  ", list));
    }

    #region True or false: is_x and has_x

    /// <summary>Verbs that ask a question about their argument, so they read as one: <c>$list->contains(3)</c>.</summary>
    private static readonly HashSet<string> VerbQuestions = new HashSet<string>(StringComparer.Ordinal)
    {
        "contains", "starts_with", "ends_with", "matches", // text and lists: does it contain, start with, end with, match this?
        "exists",                                          // @exists(path), $storage->exists(name), $query->exists(): is it there?
        "between",                                         // $n->between(1, 10)
        "any", "all",                                      // $list->any(fn), $list->all(fn)
        "in",                                              // $ip->in(block)
        "is",                                              // $error->is(NotFound), $mime->is('text/*'), Type->is(value)
        "test",                                            // $regex->test(text)
        "satisfies",                                       // $version->satisfies('^1.2')
        "verify",                                          // $password->verify(hash): is this the password?
        "needs_rehash",                                    // $password->needs_rehash(hash)
        "can", "cannot",                                   // @can('edit', $post)
        "not",                                             // $flag->not(): the operator as a method, for pipes
    };

    /// <summary>Actions that do something and say whether they did: the name is the action, not a question.</summary>
    private static readonly HashSet<string> ActionsThatReport = new HashSet<string>(StringComparer.Ordinal)
    {
        "set", "remove", "delete", "force_delete", "add", "save", "restore", "retry", "drop_table", "create_table", "revoke_token", "send", "copy", "move",
        "confirm", "assert", "to_bool", "attempt", "login", "logout", "release", "acquire",
    };

    [Fact]
    public void A_command_or_method_that_answers_true_or_false_is_named_is_x_or_has_x()
    {
        None(Callables.Where(c => c.ReturnType == KType.Bool && !IsQuestionName(c.Name)
                && !(VerbQuestions.Contains(c.Name) && (c.MaxArgs > 0 || c.Name is "exists" or "needs_rehash" or "not"))
                && !ActionsThatReport.Contains(c.Name)).Select(Show),
            "These return true or false, so their names start with is_ or has_ (or add a verb question to the list, with a reason):");
    }

    [Fact]
    public void A_field_that_is_true_or_false_is_named_is_x_or_has_x()
    {
        // exists: the same word @exists(path) and $storage->exists(name) use.
        None(Catalog.Fields.Where(f => f.Type == KType.Bool && !IsQuestionName(f.Name) && f.Name != "exists").Select(f => $"{f.Receiver}.{f.Name}"),
            "These fields are true or false, so their names start with is_ or has_:");
    }

    [Fact]
    public void Nothing_named_is_x_or_has_x_returns_anything_but_true_or_false()
    {
        None(Callables.Where(c => IsQuestionName(c.Name) && c.ReturnType != KType.Bool).Select(Show)
                .Concat(Catalog.Fields.Where(f => IsQuestionName(f.Name) && f.Type != KType.Bool).Select(f => $"{f.Receiver}.{f.Name}")),
            "A name that starts with is_ or has_ answers true or false:");
    }

    private static bool IsQuestionName(string name) =>
        name == "has" || name.StartsWith("is_", StringComparison.Ordinal) || name.StartsWith("has_", StringComparison.Ordinal);

    #endregion

    #region Descriptions say true and false

    // The word yes, unless it is quoted text a script reads ('yes'->to_bool()).
    private static readonly Regex Yes = new Regex(@"(?<!['""\w])yes(?!['""\w])", RegexOptions.IgnoreCase);

    /// <summary>Descriptions that are about a person answering yes or no, not about a boolean in a script.</summary>
    private static readonly HashSet<string> AboutAPersonsAnswer = new HashSet<string>(StringComparer.Ordinal)
    {
        "<confirm>", // asks a yes/no question at the terminal
    };

    [Fact]
    public void Descriptions_say_true_and_false_never_yes_and_no()
    {
        IEnumerable<(string Where, string Text)> all =
            Callables.Select(c => (Show(c), c.Description))
            .Concat(Callables.SelectMany(c => c.Signatures.SelectMany(s => s.Parameters).Select(p => ($"{Show(c)} {p.Name}", p.Description))))
            .Concat(Catalog.Fields.Select(f => ($"{f.Receiver}.{f.Name}", f.Description)))
            .Concat(Elements.Select(e => ($"<{e.Name}>", e.Description)))
            .Concat(Elements.SelectMany(e => e.Attributes.Select(a => ($"<{e.Name} {a.Name}>", a.Description))));
        None(all.Where(d => d.Text != null && !AboutAPersonsAnswer.Contains(d.Where) && (Yes.IsMatch(d.Text) || Regex.IsMatch(d.Text, @"(^|[.:(] )no (if|when|for)\b")))
                .Select(d => $"{d.Where}: {d.Text}"),
            "Booleans are true and false. Write \"True when ...\", not \"yes if ...\":");
    }

    [Fact]
    public void A_description_of_something_true_or_false_starts_with_True()
    {
        None(Callables.Where(c => c.ReturnType == KType.Bool && IsQuestionName(c.Name) && c.Description != null && !c.Description.StartsWith("True ", StringComparison.Ordinal)).Select(c => $"{Show(c)}: {c.Description}")
                .Concat(Catalog.Fields.Where(f => f.Type == KType.Bool && f.Description != null && !f.Description.StartsWith("True ", StringComparison.Ordinal)).Select(f => $"{f.Receiver}.{f.Name}: {f.Description}")),
            "Describe an is_x or has_x as \"True when ...\" (or \"True for ...\"):");
    }

    #endregion

    #region Fields and methods

    /// <summary>
    /// Methods with no arguments, on types of value that have fields, that are right to stay methods: each one does
    /// work every time it is called, changes something, or gives a new value made from this one. Anything else with
    /// no arguments is a plain fact, and plain facts are fields.
    /// </summary>
    private static readonly Dictionary<string, string> ZeroArgumentMethods = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // The value in another form, worked out each time (like to_text and to_json).
        ["base64->decode"] = "decodes the text",
        ["card->reveal"] = "gives the whole number, which is hidden everywhere else",
        ["color->css"] = "the colour written for CSS",
        ["color->hex"] = "the colour written as #rrggbb",
        ["color->rgb"] = "the colour as a list of three numbers",
        ["date->date"] = "a new date without the time",
        ["date->iso"] = "the date written as ISO 8601",
        ["date->timestamp"] = "the date converted to seconds since 1970",
        ["domain->ascii"] = "the name converted to punycode",
        ["duration->iso"] = "the length of time written as ISO 8601",
        ["iban->formatted"] = "the number written in groups of four",
        ["phone->tel"] = "the number written as a tel: link",
        ["document->html"] = "writes the document as HTML",
        ["document->latex"] = "writes the document as LaTeX",
        ["document->markdown"] = "writes the document as Markdown",
        ["type->schema"] = "builds the type's JSON Schema",
        ["error->chain"] = "walks the causes into a list",
        // Work that reads a file, a database, a queue or the network.
        ["file->read_text"] = "reads the file",
        ["file->read_lines"] = "reads the file",
        ["http_response->json"] = "parses the body",
        ["response->check"] = "throws unless the status is 2xx",
        ["document->pages"] = "lays the document out to count its pages",
        ["document->words"] = "counts the words in the text",
        ["database->tables"] = "asks the database",
        ["queue->size"] = "asks the queue's driver",
        ["queue->waiting"] = "asks the queue's driver",
        ["queue->failed"] = "asks the queue's driver",
        ["cache->keys"] = "reads the store, leaving out what has run out",
        ["session->all"] = "copies every value into a map",
        ["logger->entries"] = "copies what a memory provider holds",
        // Actions.
        ["cache->clear"] = "removes every key",
        ["queue->clear"] = "removes every waiting job",
        ["storage->clear"] = "deletes every file",
        ["chat->reset"] = "forgets the conversation",
        ["session->regenerate"] = "gives the session a new id",
        ["logger->flush"] = "writes out what is held back",
    };

    /// <summary>Types whose fields are the user's own (a record's columns, a setting's keys), so their own facts stay methods.</summary>
    private static readonly HashSet<string> FieldsAreTheUsersOwn = new HashSet<string>(StringComparer.Ordinal)
    {
        KType.Record, KType.Map, KType.Any,
    };

    [Fact]
    public void A_plain_fact_about_a_value_is_a_field_not_a_method_with_no_arguments()
    {
        None(Catalog.Methods.Where(m => !m.Planned && m.MaxArgs == 0 && Catalog.FieldsOf(m.ReceiverType).Count > 0 && !FieldsAreTheUsersOwn.Contains(m.ReceiverType)
                && !ConvertsOrAsks(m.Name) && !ZeroArgumentMethods.ContainsKey($"{m.ReceiverType}->{m.Name}")).Select(Show),
            "These take no arguments on a type that has fields. Make each a field (p.Field), or add it to ZeroArgumentMethods with the work it does:");
    }

    [Fact]
    public void No_method_has_the_name_of_a_field_of_the_same_type()
    {
        None(Catalog.Methods.Where(m => !m.Planned && Catalog.GetField(m.ReceiverType, m.Name) != null).Select(Show),
            "A name is a field or a method of a type, never both:");
    }

    [Fact]
    public void The_allow_list_of_methods_with_no_arguments_has_no_stale_entries()
    {
        HashSet<string> real = Catalog.Methods.Where(m => !m.Planned && m.MaxArgs == 0).Select(Show).Select(s => s.Substring(0, s.Length - 2)).ToHashSet(StringComparer.Ordinal);
        None(ZeroArgumentMethods.Keys.Where(k => !real.Contains(k)), "These are no longer methods with no arguments; take them off the list:");
    }

    // to_x gives the value in another form, is_x and has_x ask a question: both are methods everywhere.
    private static bool ConvertsOrAsks(string name) =>
        name.StartsWith("to_", StringComparison.Ordinal) || name.StartsWith("as_", StringComparison.Ordinal) || IsQuestionName(name);

    #endregion

    #region get, set, has, remove, delete, clear, flush

    [Fact]
    public void Keyed_things_use_get_set_has_and_remove()
    {
        None(Callables.Where(c => c.Name is "put" or "forget" or "unset" or "erase" or "purge" or "wipe" or "empty" or "reset_all").Select(Show),
            "Things kept by key use get, set, has and remove, and clear empties them. These use another verb:");
    }

    [Fact]
    public void Flush_only_writes_out_what_is_held_back()
    {
        // The logger holds entries back and flush writes them. Nothing else may use the word.
        None(Callables.Where(c => c.Name == "flush" && Show(c) != "logger->flush()").Select(Show),
            "flush means \"write out what is being held back\". To remove everything, the word is clear:");
    }

    [Fact]
    public void A_type_with_get_and_set_removes_with_remove()
    {
        // Storage reads and writes files, so it has read, write and delete, not get and set.
        foreach (string type in Catalog.MethodReceivers)
        {
            HashSet<string> names = Catalog.Methods.Where(m => !m.Planned && m.ReceiverType == type).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            if (!names.Contains("get") || !names.Contains("set")) continue;
            Assert.False(names.Contains("delete"), $"{type} has get and set, so it removes a key with remove, not delete.");
            Assert.True(names.Contains("has"), $"{type} has get and set, so it needs has.");
            Assert.True(names.Contains("remove"), $"{type} has get and set, so it needs remove.");
        }
    }

    [Fact]
    public void Old_verbs_say_what_to_write_instead()
    {
        Assert.Equal("set", RetiredMembers.Method("cache", "put").Rename);
        Assert.Equal("remove", RetiredMembers.Method("cache", "forget").Rename);
        Assert.Equal("clear", RetiredMembers.Method("cache", "flush").Rename);
        Assert.Equal("set", RetiredMembers.Method("session", "put").Rename);
        Assert.Equal("remove", RetiredMembers.Method("session", "forget").Rename);
        // Nothing retired is still registered.
        None(Catalog.Methods.Where(m => !m.Planned && RetiredMembers.Method(m.ReceiverType, m.Name) != null).Select(Show), "These are both retired and registered:");
    }

    #endregion

    #region Lengths of time are durations

    private static readonly Regex TimeName = new Regex("^(timeout|ttl|every|delay|wait|backoff|retry_after|retry_until|unique_for|lockout|duration|expires|.+_(for|after|until|timeout|ttl))$");

    /// <summary>Arguments with a name that sounds like a length of time but that hold something else.</summary>
    private static readonly Dictionary<string, string> NotALengthOfTime = new Dictionary<string, string>(StringComparer.Ordinal)
    {
    };

    private static readonly HashSet<string> UnitNames = new HashSet<string>(StringComparer.Ordinal) { "ms", "milliseconds", "seconds", "secs", "minutes", "hours" };

    [Fact]
    public void Every_length_of_time_is_declared_as_a_duration()
    {
        IEnumerable<string> attributes = Elements.SelectMany(e => e.Attributes.Where(a => TimeName.IsMatch(a.Name) && a.Type != Duration.TypeName).Select(a => $"<{e.Name} {a.Name}>"));
        IEnumerable<string> parameters = Callables.SelectMany(c => c.Signatures.SelectMany(s => s.Parameters)
            .Where(p => TimeName.IsMatch(p.Name) && p.Type != Duration.TypeName).Select(p => $"{Show(c)} {p.Name}"));
        None(attributes.Concat(parameters).Where(n => !NotALengthOfTime.ContainsKey(n)),
            "A length of time is a duration (30s, 5m, 1h30m). Declare these with the duration type, or add them to NotALengthOfTime with what they hold:");
    }

    [Fact]
    public void No_argument_is_named_for_a_unit_of_time()
    {
        IEnumerable<string> attributes = Elements.SelectMany(e => e.Attributes.Where(a => UnitNames.Contains(a.Name)).Select(a => $"<{e.Name} {a.Name}>"));
        // $date->add_hours(hours) counts hours: the unit is the method's own name, not a second way to give a length of time.
        IEnumerable<string> parameters = Callables.Where(c => !c.Name.StartsWith("add_", StringComparison.Ordinal))
            .SelectMany(c => c.Signatures.SelectMany(s => s.Parameters).Where(p => UnitNames.Contains(p.Name)).Select(p => $"{Show(c)} {p.Name}"));
        None(attributes.Concat(parameters), "The unit is part of the value (wait(duration: 500ms)), not of the argument's name:");
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("500ms", 0.5)]
    [InlineData("1h30m", 5400)]
    [InlineData("2d", 172800)]
    [InlineData("1.5s", 1.5)]
    public void A_duration_is_written_without_quotes_and_a_plain_number_is_seconds(string written, double seconds)
    {
        string path = Path.Combine(Path.GetTempPath(), "durations.kscript");
        Assert.Equal($"duration {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n", ScriptRunner.Run($"d = {written}\necho \"{{ $d->type() }} {{ $d.seconds }}\"\n", path: path));
        Assert.True(Duration.TrySeconds(seconds, out double plain));
        Assert.Equal(seconds, plain);
    }

    #endregion

    #region Tables

    [Fact]
    public void A_table_takes_the_same_arguments_in_the_terminal_and_in_a_document()
    {
        ElementInfo terminal = Elements.Single(e => e.Name == "table");
        ElementInfo document = Elements.Single(e => e.Name == "Table");
        foreach (string name in new[] { "rows", "columns", "align", "header" })
        {
            AttributeInfo a = terminal.GetAttribute(name), b = document.GetAttribute(name);
            Assert.True(a != null, $"<table> needs {name}.");
            Assert.True(b != null, $"<Table> needs {name}.");
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.Description, b.Description);
        }
        foreach (string old in new[] { "data", "headers", "items", "head", "heading", "headings" })
        {
            Assert.Null(terminal.GetAttribute(old));
            Assert.Null(document.GetAttribute(old));
        }
    }

    #endregion
}
