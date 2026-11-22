using KScript.Language.Catalog;
using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Tests.Conventions;

using Conventions = KScript.Runtime.Conventions;

/// <summary>
/// One name per thing in the standard library. These walk every command and method of every package that ships
/// with kscript, so a second spelling (colour, ->length(), @to_upper()) fails here instead of reaching users.
/// The rules are in <see cref="Conventions"/>, which the engine applies as each package registers; these tests apply
/// them to everything that ships, one rule at a time.
/// </summary>
public class StdlibNameConventionTests
{
    private static readonly IReadOnlyList<KScriptPackage> Packages = PackageRegistry.Default.LoadAll().ToList();

    private static IEnumerable<CallableInfo> Commands => Packages.SelectMany(p => p.Commands.Values).Select(c => c.Info);

    private static IEnumerable<CallableInfo> Methods => Packages.SelectMany(p => p.Methods).Select(m => m.Info);

    private static IEnumerable<ElementInfo> Elements => Packages.SelectMany(p => p.Elements.Values).Select(e => e.Info);

    private static IReadOnlyList<ParameterInfo> Parameters(CallableInfo c) => c.Signatures.SelectMany(s => s.Parameters).ToList();

    /// <summary>What a rule of <see cref="Conventions"/> says about each command and method: nothing, when all is well.</summary>
    private static IEnumerable<string> Broken(Func<string, CallableInfo, string> rule) =>
        Commands.Concat(Methods).Select(c => rule(c.Package, c)).Where(problem => problem != null);

    /// <summary>What every rule that spans registrations says about the packages that ship, and what the tables in Conventions and Retired say.</summary>
    private static readonly IReadOnlyList<string> Together = Conventions.Together(Packages);

    private static readonly IReadOnlyList<string> Tables = Conventions.Tables(Packages);

    private static void None(IEnumerable<string> problems, string rule)
    {
        List<string> found = problems.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(found.Count == 0, rule + "\n  " + string.Join("\n  ", found));
    }

    [Fact]
    public void Every_package_that_ships_is_checked()
    {
        Assert.Equal(PackageRegistry.BuiltIn.Select(p => p.Name).OrderBy(n => n), Packages.Select(p => p.Name).OrderBy(n => n));
        Assert.True(Commands.Count() > 100 && Methods.Count() > 300, "The packages should register their commands and methods.");
    }

    [Fact]
    public void Commands_methods_and_their_parameters_are_snake_case()
    {
        None(Broken(Conventions.CallableName), "Command and method names are snake_case (letters, digits and _, starting with a letter):");
        None(Commands.Concat(Methods).SelectMany(c => Parameters(c).Select(p => Conventions.ParameterName(c.Package, c, p))).Where(problem => problem != null),
            "Parameter names are snake_case:");
    }

    [Fact]
    public void No_identifier_says_colour()
    {
        var problems = new List<string>();
        foreach (CallableInfo c in Commands.Concat(Methods))
        {
            problems.Add(Conventions.Color(c.Package, Conventions.Key(c), c.Name));
            problems.AddRange(Parameters(c).Select(p => Conventions.ParameterColor(c.Package, c, p)));
        }
        foreach (ElementInfo e in Elements)
        {
            problems.Add(Conventions.Color(e.Package, $"<{e.Name}>", e.Name));
            problems.AddRange(e.Attributes.Select(a => Conventions.ArgumentColor(e.Package, e, a)));
        }
        foreach (KScriptPackage p in Packages)
        {
            problems.AddRange(p.Fields.Select(f => Conventions.Color(p.Name, $"{f.Receiver}.{f.Name}", f.Name)));
            problems.AddRange(p.Variables.Keys.Select(v => Conventions.Color(p.Name, $"${v}", v)));
            problems.AddRange(p.LogProviders.Keys.Select(v => Conventions.Color(p.Name, $"log provider {v}", v)));
            problems.AddRange(p.EchoFormats.Keys.Select(v => Conventions.Color(p.Name, $"echo format {v}", v)));
        }
        problems.AddRange(NativeTypes.Names.Select(n => Conventions.Color(null, $"type {n}", n)));
        None(problems.Where(problem => problem != null), "Names say color (as CSS and the color type do); colour is for prose only:");
    }

    [Fact]
    public void No_command_or_method_is_described_as_another_name_for_something() =>
        // "(the same as filter)" and "an alias of" are how a second name for one thing announces itself.
        None(Broken(Conventions.SecondName), "A command or method that is the same as another one is a second spelling. Keep one:");

    [Fact]
    public void No_two_commands_and_no_two_methods_of_a_type_share_a_description() =>
        None(Together.Where(problem => problem.Contains("it has the description of")), "Two names with one description are two spellings of one thing. Keep one (or say how they differ):");

    [Fact]
    public void No_method_is_registered_twice_for_a_type() =>
        None(Together.Where(problem => problem.Contains("already registered")), "A type has each method once, from one package:");

    [Fact]
    public void Count_takes_no_arguments()
    {
        // ->count() is "how many" on every value. "How many match" is ->count_of(match). There is no ->length().
        None(Broken(Conventions.Count), "->count() takes no arguments; counting what matches is ->count_of(match), and the number of items or characters is ->count(), not ->length():");
        // A list, a text and a map have no fields of their own (a map's are the user's keys), so "how many" is the
        // method on all of them and on everything else that is counted. See "Fields and methods" in
        // StdlibBehaviourConventionTests: a name is a field or a method, never both.
        None(LanguageCatalog.Default.Fields.Select(f => Conventions.CountField(f.Package, f)).Where(problem => problem != null),
            "\"How many\" is the method ->count() on every type, never a field:");
        Assert.Contains("->count()", ScriptRunner.Fails("<echo>{ [1, 2].count }</echo>").Message);
        Assert.Contains("->count()", ScriptRunner.Fails("<echo>{ 'abc'.length }</echo>").Message);
    }

    [Fact]
    public void Retired_names_stay_retired()
    {
        // A KScript 1 command whose name a package uses again (@average in KScript.Math) says so in its replacement.
        None(Broken(Conventions.RetiredCallable), "These are in Retired.Commands or RetiredMembers.Methods, which tell users what to write instead. Don't bring the old name back:");
        None(Elements.Select(e => Conventions.RetiredElement(e.Package, e)).Where(problem => problem != null),
            "These statements are in Retired.Elements. Don't bring the old name back:");
        None(Elements.SelectMany(e => e.Attributes.Select(a => Conventions.RetiredArgument(e.Package, e, a))).Where(problem => problem != null),
            "These arguments are in Retired.RenamedAttributes. Don't bring the old name back:");
        // Every new name a retired one points at exists, on the same type for a method (a rename that starts with
        // a dot is a field: ->ok() became .is_ok).
        None(Tables.Where(problem => problem.StartsWith("Retired", StringComparison.Ordinal)), "A retired name's new name should exist:");
    }

    #region Commands that repeat a method

    [Fact]
    public void No_command_repeats_a_method_of_its_first_argument()
    {
        // The pairs that are there on purpose are Conventions.DeliberatePairs, each with its reason.
        None(Together.Where(problem => problem.Contains("it only repeats the method")), "Methods are the primary form. These commands only repeat a method of their first argument:");
        None(Tables.Where(problem => problem.StartsWith("Conventions.DeliberatePairs", StringComparison.Ordinal)), "These DeliberatePairs entries no longer match anything. Remove them:");
        Assert.True(Conventions.DeliberatePairs.Count <= 20, "DeliberatePairs is meant to stay short.");
        Assert.All(Conventions.DeliberatePairs.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    [Fact]
    public void A_command_and_its_method_use_the_same_parameter_names() =>
        None(Together.Where(problem => problem.Contains("take the same parameter names")), "A command and the method of the same name take the same parameter names after the value:");

    [Fact]
    public void A_name_is_a_statement_or_a_command_not_both()
    {
        // The names that are both on purpose are Conventions.StatementAndCommand, each with its reason.
        None(Together.Where(problem => problem.Contains("An action has one form")), "An action has one form, a statement or a command:");
        None(Tables.Where(problem => problem.StartsWith("Conventions.StatementAndCommand", StringComparison.Ordinal)), "These StatementAndCommand entries are stale:");
        Assert.True(Conventions.StatementAndCommand.Count <= 5, "StatementAndCommand is meant to stay short.");
    }

    #endregion
}
