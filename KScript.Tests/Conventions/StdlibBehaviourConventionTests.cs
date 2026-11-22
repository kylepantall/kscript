using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Tests.Conventions;

using Conventions = KScript.Runtime.Conventions;

/// <summary>
/// How the standard packages behave, checked over everything every package registers, so one package can't drift
/// from the rest: names that answer true or false, fields and methods, the verbs for keyed things, lengths of
/// time, and the arguments a table takes. The rules are written out in docs/contributing.md ("How the standard
/// packages behave"). The rules and their allow-lists (each short, each saying why a name is on it) are in
/// <see cref="Conventions"/>, which the engine applies as each package registers; these tests apply them to the whole
/// catalog, one rule at a time.
/// </summary>
public class StdlibBehaviourConventionTests
{
    private static readonly LanguageCatalog Catalog = LanguageCatalog.Default;

    private static IEnumerable<CallableInfo> Callables =>
        Catalog.Commands.Concat(Catalog.Methods).Where(c => !c.Planned && c.Kind != CallableKind.Function);

    private static IEnumerable<ElementInfo> Elements => Catalog.Elements.Where(e => !e.Planned);

    private static string Show(CallableInfo c) => c.Kind == CallableKind.Method ? $"{c.ReceiverType}->{c.Name}()" : $"@{c.Name}()";

    private static readonly IReadOnlyList<KScriptPackage> Packages = PackageRegistry.Default.LoadAll().ToList();

    /// <summary>What every rule that spans registrations says about the packages that ship, and what the tables in Conventions say.</summary>
    private static readonly IReadOnlyList<string> Together = Conventions.Together(Packages);

    private static readonly IReadOnlyList<string> Tables = Conventions.Tables(Packages);

    /// <summary>What a rule of <see cref="Conventions"/> says about each command and method: nothing, when all is well.</summary>
    private static IEnumerable<string> Broken(Func<string, CallableInfo, string> rule) =>
        Callables.Select(c => rule(c.Package, c)).Where(problem => problem != null);

    private static IEnumerable<string> Broken(Func<string, ValueFieldInfo, string> rule) =>
        Catalog.Fields.Select(f => rule(f.Package, f)).Where(problem => problem != null);

    private static void None(IEnumerable<string> broken, string rule)
    {
        List<string> list = broken.Distinct().OrderBy(b => b, StringComparer.Ordinal).ToList();
        Assert.True(list.Count == 0, rule + "\n  " + string.Join("\n  ", list));
    }

    #region True or false: is_x and has_x

    [Fact]
    public void A_command_or_method_that_answers_true_or_false_is_named_is_x_or_has_x() =>
        None(Broken(Conventions.AnswersTrueOrFalse),
            "These return true or false, so their names start with is_ or has_ (or add a verb question to Conventions.VerbQuestions, with a reason):");

    [Fact]
    public void A_field_that_is_true_or_false_is_named_is_x_or_has_x() =>
        // exists: the same word @exists(path) and $storage->exists(name) use.
        None(Broken(Conventions.FieldTrueOrFalse), "These fields are true or false, so their names start with is_ or has_:");

    [Fact]
    public void Nothing_named_is_x_or_has_x_returns_anything_but_true_or_false() =>
        None(Broken(Conventions.QuestionGivesTrueOrFalse).Concat(Broken(Conventions.QuestionFieldIsTrueOrFalse)),
            "A name that starts with is_ or has_ answers true or false:");

    [Fact]
    public void The_lists_of_names_that_answer_without_is_stay_short()
    {
        Assert.True(Conventions.VerbQuestions.Count <= 20, "VerbQuestions is meant to stay short.");
        Assert.True(Conventions.ActionsThatReport.Count <= 25, "ActionsThatReport is meant to stay short.");
    }

    #endregion

    #region Descriptions say true and false

    [Fact]
    public void Descriptions_say_true_and_false_never_yes_and_no()
    {
        // <confirm> asks a person a yes/no question at the terminal; it is the one description about a person's answer.
        Assert.Equal(new[] { "confirm" }, Conventions.AboutAPersonsAnswer);
        IEnumerable<string> all =
            Callables.Select(c => Conventions.TrueAndFalse(c.Package, Show(c), c.Description))
            .Concat(Callables.SelectMany(c => c.Signatures.SelectMany(s => s.Parameters).Select(p => Conventions.TrueAndFalse(c.Package, $"{Show(c)} {p.Name}", p.Description))))
            .Concat(Catalog.Fields.Select(f => Conventions.TrueAndFalse(f.Package, $"{f.Receiver}.{f.Name}", f.Description)))
            .Concat(Elements.Select(e => Conventions.TrueAndFalse(e.Package, $"<{e.Name}>", e.Description, Conventions.AboutAPersonsAnswer.Contains(e.Name))))
            .Concat(Elements.SelectMany(e => e.Attributes.Select(a => Conventions.TrueAndFalse(e.Package, $"<{e.Name} {a.Name}>", a.Description))));
        None(all.Where(problem => problem != null), "Booleans are true and false. Write \"True when ...\", not \"yes if ...\":");
    }

    [Theory]
    [InlineData("yes if the file is there", true)]
    [InlineData("Says Yes or No.", true)]
    [InlineData("True when it is there. no if it isn't", true)]
    [InlineData("The mode: no when it is off", true)]
    [InlineData("Reads 'yes' and \"yes\" as true: 'yes'->to_bool().", false)]
    [InlineData("Yesterday's eyes: there is no file for it.", false)]
    [InlineData("True when the file is there.", false)]
    public void The_check_for_yes_and_no_finds_them(string description, bool found) =>
        Assert.Equal(found, Conventions.TrueAndFalse("KScript.X", "x", description) != null);

    [Fact]
    public void A_description_of_something_true_or_false_starts_with_True() =>
        None(Broken(Conventions.StartsWithTrue).Concat(Broken(Conventions.FieldStartsWithTrue)),
            "Describe an is_x or has_x as \"True when ...\" (or \"True for ...\"):");

    #endregion

    #region Fields and methods

    [Fact]
    public void A_plain_fact_about_a_value_is_a_field_not_a_method_with_no_arguments() =>
        // The methods with no arguments that are right to stay methods are Conventions.ZeroArgumentMethods, each with the work it does.
        None(Together.Where(problem => problem.Contains("it takes no arguments on a type that has fields")),
            "These take no arguments on a type that has fields. Make each a field (p.Field), or add it to Conventions.ZeroArgumentMethods with the work it does:");

    [Fact]
    public void No_method_has_the_name_of_a_field_of_the_same_type()
    {
        None(Together.Where(problem => problem.Contains("A name is a field or a method of a type, never both")), "A name is a field or a method of a type, never both:");
        None(Catalog.Methods.Where(m => !m.Planned && Catalog.GetField(m.ReceiverType, m.Name) != null).Select(Show),
            "A name is a field or a method of a type, never both:");
    }

    [Fact]
    public void The_allow_list_of_methods_with_no_arguments_has_no_stale_entries() =>
        None(Tables.Where(problem => problem.StartsWith("Conventions.ZeroArgumentMethods", StringComparison.Ordinal)), "These are no longer methods with no arguments; take them off the list:");

    #endregion

    #region get, set, has, remove, delete, clear, flush

    [Fact]
    public void Keyed_things_use_get_set_has_and_remove() =>
        None(Broken(Conventions.KeyedVerb), "Things kept by key use get, set, has and remove, and clear empties them. These use another verb:");

    [Fact]
    public void Flush_only_writes_out_what_is_held_back() =>
        // The logger holds entries back and flush writes them. Nothing else may use the word.
        None(Broken(Conventions.Flush), "flush means \"write out what is being held back\". To remove everything, the word is clear:");

    [Fact]
    public void A_type_with_get_and_set_removes_with_remove() =>
        // Storage reads and writes files, so it has read, write and delete, not get and set.
        None(Together.Where(problem => problem.Contains("has get and set")), "A type with get and set has has and remove, and no delete:");

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

    [Fact]
    public void Every_length_of_time_is_declared_as_a_duration()
    {
        IEnumerable<string> attributes = Elements.SelectMany(e => e.Attributes.Select(a => Conventions.LengthOfTimeArgument(e.Package, e, a)));
        IEnumerable<string> parameters = Callables.SelectMany(c => c.Signatures.SelectMany(s => s.Parameters).Select(p => Conventions.LengthOfTimeParameter(c.Package, c, p)));
        None(attributes.Concat(parameters).Where(problem => problem != null),
            "A length of time is a duration (30s, 5m, 1h30m). Declare these with the duration type, or add them to Conventions.NotALengthOfTime with what they hold:");
    }

    [Theory]
    [InlineData("timeout", true)]
    [InlineData("ttl", true)]
    [InlineData("retry_after", true)]
    [InlineData("unique_for", true)]
    [InlineData("lock_until", true)]
    [InlineData("read_timeout", true)]
    [InlineData("cache_ttl", true)]
    [InlineData("_for", false)]
    [InlineData("format", false)]
    [InlineData("before", false)]
    public void A_name_that_is_a_length_of_time_is_recognised(string name, bool time) => Assert.Equal(time, Conventions.IsTimeName(name));

    [Fact]
    public void No_argument_is_named_for_a_unit_of_time()
    {
        IEnumerable<string> attributes = Elements.SelectMany(e => e.Attributes.Select(a => Conventions.UnitArgument(e.Package, e, a)));
        // $date->add_hours(hours) counts hours: the unit is the method's own name, not a second way to give a length of time.
        IEnumerable<string> parameters = Callables.SelectMany(c => c.Signatures.SelectMany(s => s.Parameters).Select(p => Conventions.UnitParameter(c.Package, c, p)));
        None(attributes.Concat(parameters).Where(problem => problem != null), "The unit is part of the value (wait(duration: 500ms)), not of the argument's name:");
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
