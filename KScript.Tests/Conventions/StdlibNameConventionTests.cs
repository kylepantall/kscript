using System.Text.RegularExpressions;

using KScript.Metadata;
using KScript.Runtime;

namespace KScript.Tests.Conventions;

/// <summary>
/// One name per thing in the standard library. These walk every command and method of every package that ships
/// with kscript, so a second spelling (colour, ->length(), @to_upper()) fails here instead of reaching users.
/// </summary>
public class StdlibNameConventionTests
{
    private static readonly Regex SnakeCase = new Regex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<KScriptPackage> Packages = PackageRegistry.Default.LoadAll().ToList();

    private static IEnumerable<CallableInfo> Commands => Packages.SelectMany(p => p.Commands.Values).Select(c => c.Info);

    private static IEnumerable<CallableInfo> Methods => Packages.SelectMany(p => p.Methods).Select(m => m.Info);

    private static IEnumerable<ElementInfo> Elements => Packages.SelectMany(p => p.Elements.Values).Select(e => e.Info);

    private static string Show(CallableInfo c) =>
        (c.Kind == CallableKind.Command ? "@" + c.Name : c.ReceiverType + "->" + c.Name) + "(" + string.Join(", ", Parameters(c).Select(p => p.Label)) + ") in " + c.Package;

    private static IReadOnlyList<ParameterInfo> Parameters(CallableInfo c) => c.Signatures.SelectMany(s => s.Parameters).ToList();

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
        None(Commands.Concat(Methods).Where(c => !SnakeCase.IsMatch(c.Name)).Select(Show),
            "Command and method names are snake_case (letters, digits and _, starting with a letter):");
        None(Commands.Concat(Methods).SelectMany(c => Parameters(c).Where(p => !SnakeCase.IsMatch(p.Name)).Select(p => $"{p.Name} of {Show(c)}")),
            "Parameter names are snake_case:");
    }

    [Fact]
    public void No_identifier_says_colour()
    {
        static bool Colour(string name) => name != null && name.Contains("colour", StringComparison.OrdinalIgnoreCase);

        var problems = new List<string>();
        foreach (CallableInfo c in Commands.Concat(Methods))
        {
            if (Colour(c.Name)) problems.Add(Show(c));
            problems.AddRange(Parameters(c).Where(p => Colour(p.Name) || p.Values.Any(Colour)).Select(p => $"{p.Name} of {Show(c)}"));
        }
        foreach (ElementInfo e in Elements)
        {
            if (Colour(e.Name)) problems.Add($"<{e.Name}> in {e.Package}");
            problems.AddRange(e.Attributes.Where(a => Colour(a.Name) || a.Options.Any(Colour)).Select(a => $"{a.Name} of <{e.Name}> in {e.Package}"));
        }
        foreach (KScriptPackage p in Packages)
        {
            problems.AddRange(p.Variables.Keys.Where(Colour).Select(v => $"${v} in {p.Name}"));
            problems.AddRange(p.LogProviders.Keys.Where(Colour).Select(v => $"log provider {v} in {p.Name}"));
            problems.AddRange(p.EchoFormats.Keys.Where(Colour).Select(v => $"echo format {v} in {p.Name}"));
        }
        problems.AddRange(NativeTypes.Names.Where(Colour).Select(n => $"type {n}"));
        None(problems, "Names say color (as CSS and the color type do); colour is for prose only:");
    }

    [Fact]
    public void No_command_or_method_is_described_as_another_name_for_something()
    {
        // "(the same as filter)" and "an alias of" are how a second name for one thing announces itself.
        var second = new Regex(@"\bthe same as\b|\balias(es)?\b|\bshort for\b|\bsynonym", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        None(Commands.Concat(Methods).Where(c => second.IsMatch(c.Description ?? string.Empty)).Select(c => $"{Show(c)}: {c.Description}"),
            "A command or method that is the same as another one is a second spelling. Keep one:");
    }

    [Fact]
    public void No_two_commands_and_no_two_methods_of_a_type_share_a_description()
    {
        static string Key(string description) => Regex.Replace(description ?? string.Empty, @"\s+", " ").Trim().TrimEnd('.').ToLowerInvariant();

        var problems = new List<string>();
        foreach (var same in Commands.Where(c => !string.IsNullOrWhiteSpace(c.Description)).GroupBy(c => Key(c.Description)).Where(g => g.Count() > 1))
        {
            problems.Add(string.Join(" and ", same.Select(Show)) + $": \"{same.First().Description}\"");
        }
        foreach (var same in Methods.Where(m => !string.IsNullOrWhiteSpace(m.Description)).GroupBy(m => m.ReceiverType + "\n" + Key(m.Description)).Where(g => g.Count() > 1))
        {
            problems.Add(string.Join(" and ", same.Select(Show)) + $": \"{same.First().Description}\"");
        }
        None(problems, "Two names with one description are two spellings of one thing. Keep one (or say how they differ):");
    }

    [Fact]
    public void No_method_is_registered_twice_for_a_type()
    {
        None(Methods.GroupBy(m => m.ReceiverType + "->" + m.Name).Where(g => g.Count() > 1).Select(g => string.Join(" and ", g.Select(Show))),
            "A type has each method once, from one package:");
    }

    [Fact]
    public void Count_takes_no_arguments()
    {
        // ->count() is "how many" on every value. "How many match" is ->count_of(match).
        None(Methods.Where(m => m.Name == "count" && Parameters(m).Count > 0).Select(Show),
            "->count() takes no arguments; counting what matches is ->count_of(match):");
        None(Methods.Where(m => m.Name == "length").Select(Show), "The number of items or characters is ->count(), not ->length():");
    }

    [Fact]
    public void Retired_names_stay_retired()
    {
        // A KScript 1 command whose name a package uses again (@average in KScript.Math) says so in its replacement.
        None(Commands.Where(c => Retired.Commands.TryGetValue(c.Name, out RetiredInfo r) && !r.Replacement.Contains("@" + c.Name + "(", StringComparison.Ordinal)).Select(Show),
            "These commands are in Retired.Commands, which tells users what to write instead. Don't bring the old name back:");
        None(Methods.Where(m => Retired.Method(m.ReceiverType, m.Name) != null).Select(Show),
            "These methods are in Retired.Methods. Don't bring the old name back:");
        None(Elements.Where(e => Retired.Elements.ContainsKey(e.Name)).Select(e => $"<{e.Name}> in {e.Package}"),
            "These statements are in Retired.Elements. Don't bring the old name back:");
        None(Retired.Methods.Values.Where(r => r.Rename != null).Select(r => (Retired: r, Type: r.Name.Substring(0, r.Name.IndexOf("->", StringComparison.Ordinal))))
                .Where(x => !Methods.Any(m => m.Name == x.Retired.Rename && KType.AcceptsMethod(m.ReceiverType, m.Name, x.Type)))
                .Select(x => $"{x.Retired.Name} -> {x.Retired.Rename}"),
            "A retired method's new name should exist on the same type:");
        None(Retired.Commands.Values.Where(r => r.Rename != null && !Commands.Any(c => c.Name == r.Rename)).Select(r => $"@{r.Name} -> @{r.Rename}"),
            "A retired command's new name should exist:");
    }

    #region Commands that repeat a method

    /// <summary>
    /// The pairs where a command and a method of its first argument's type do the same thing on purpose. Methods are
    /// the primary form, so every entry needs a reason. Keys are "@command = type->method".
    /// </summary>
    private static readonly Dictionary<string, string> DeliberatePairs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // KScript.Math: formulas read as functions, @sqrt(@abs($x)), as they do in maths. Core's number and list
        // methods cover the everyday cases without importing the package.
        ["@sqrt = number->sqrt"] = "maths function",
        ["@abs = number->abs"] = "maths function",
        ["@floor = number->floor"] = "maths function",
        ["@ceil = number->ceil"] = "maths function",
        ["@round = number->round"] = "maths function",
        ["@clamp = number->clamp"] = "maths function",
        ["@sum = list->sum"] = "takes any number of values: @sum(1, 2, 3)",
        ["@average = list->average"] = "takes any number of values",
        ["@median = list->median"] = "takes any number of values",
        ["@stdev = list->stdev"] = "takes any number of values",
        ["@min = list->min"] = "takes any number of values: @min($a, $b, 0)",
        ["@max = list->max"] = "takes any number of values",

        // KScript.Style: inside a stylesheet a colour is CSS text (a name, hsl(), a variable's value), not a color value.
        ["@lighten = color->lighten"] = "CSS colour text in stylesheets",
        ["@darken = color->darken"] = "CSS colour text in stylesheets",
        ["@mix = color->mix"] = "CSS colour text in stylesheets",

        // KScript.Markdown: the command reads a whole document (title, front matter, html); the method gives only the HTML.
        ["@markdown = string->markdown"] = "document map against HTML text",
    };

    /// <summary>The type of a command's first argument: the declared one, or what its name says by convention.</summary>
    private static string FirstArgumentType(CallableInfo command)
    {
        ParameterInfo first = command.Signatures.FirstOrDefault()?.Parameters.FirstOrDefault();
        if (first == null) return null;
        if (first.Variadic) return first.Name == "values" ? KType.List : null;
        string declared = first.Type switch { "int" => KType.Number, null => null, string t => t };
        if (declared != null) return declared;
        return first.Name switch
        {
            "text" => KType.String,
            "number" or "n" or "x" or "a" => KType.Number,
            "list" => KType.List,
            "map" => KType.Map,
            "date" => KType.Date,
            "color" => "color",
            "value" => KType.Any,
            _ => null,
        };
    }

    private static string Meaning(string description) => Regex.Replace(description ?? string.Empty, @"\s+", " ").Trim().TrimEnd('.').ToLowerInvariant();

    /// <summary>Every command paired with a method of its first argument's type that has its name (or its to_ form) or its description.</summary>
    private static IEnumerable<(CallableInfo Command, CallableInfo Method)> Pairs()
    {
        List<CallableInfo> methods = Methods.ToList();
        foreach (CallableInfo command in Commands)
        {
            string type = FirstArgumentType(command);
            if (type == null) continue;
            foreach (CallableInfo method in methods.Where(m => m.ReceiverType == type || (m.ReceiverType == KType.Any && type != null)))
            {
                bool sameName = method.Name == command.Name || method.Name == "to_" + command.Name || "to_" + method.Name == command.Name;
                bool sameMeaning = Meaning(method.Description).Length > 0 && Meaning(method.Description) == Meaning(command.Description);
                if (sameName || sameMeaning) yield return (command, method);
            }
        }
    }

    private static string PairKey((CallableInfo Command, CallableInfo Method) pair) => $"@{pair.Command.Name} = {pair.Method.ReceiverType}->{pair.Method.Name}";

    [Fact]
    public void No_command_repeats_a_method_of_its_first_argument()
    {
        List<string> pairs = Pairs().Select(PairKey).Distinct().ToList();
        None(pairs.Where(p => !DeliberatePairs.ContainsKey(p)).Select(p => p + "  (write $value->method(); drop the command, or add the pair to DeliberatePairs with a reason)"),
            "Methods are the primary form. These commands only repeat a method of their first argument:");
        None(DeliberatePairs.Keys.Where(k => !pairs.Contains(k)), "These DeliberatePairs entries no longer match anything. Remove them:");
        Assert.True(DeliberatePairs.Count <= 20, "DeliberatePairs is meant to stay short.");
    }

    [Fact]
    public void A_command_and_its_method_use_the_same_parameter_names()
    {
        var problems = new List<string>();
        foreach (var (command, method) in Pairs().Where(p => p.Command.Name == p.Method.Name))
        {
            // @clamp(number, min, max) and $n->clamp(min, max): after the value itself, the names line up.
            // Optional parameters at the end may be missing on either side.
            List<ParameterInfo> rest = command.Signatures.First().Parameters.Skip(1).ToList();
            List<ParameterInfo> own = method.Signatures.First().Parameters.ToList();
            int shared = Math.Min(rest.Count, own.Count);
            bool lineUp = rest.Take(shared).Select(p => p.Name).SequenceEqual(own.Take(shared).Select(p => p.Name))
                && rest.Skip(shared).Concat(own.Skip(shared)).All(p => p.Optional || p.Variadic);
            if (!lineUp) problems.Add($"{Show(command)} and {Show(method)}");
        }
        None(problems, "A command and the method of the same name take the same parameter names after the value:");
    }

    #endregion
}
