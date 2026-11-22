using KScript.Metadata;
using KScript.Runtime;

using A = KScript.Metadata.AttributeInfo;
using K = KScript.Metadata.AttributeKind;

namespace KScript.Tests.Conventions;

using Conventions = KScript.Runtime.Conventions;

/// <summary>
/// The engine refuses a built-in that breaks a naming or shape rule. The rules are in
/// <see cref="KScript.Runtime.Conventions"/>, and the package builder applies them to every registration, so a
/// package that breaks one throws a <see cref="ConventionException"/> as it loads. These tests are the proof of
/// both halves: every package that ships loads clean, and each rule fires on a throwaway package that registers
/// something deliberately wrong, with a message that names the package, the name and the rule.
/// </summary>
public class ConventionEngineTests
{
    private static readonly IReadOnlyList<KScriptPackage> Packages = PackageRegistry.Default.LoadAll().ToList();

    private static object Nothing(CallContext call) => null;

    private static object Nothing(object target, CallContext call) => null;

    /// <summary>The message a throwaway package is refused with when it registers this.</summary>
    private static string Refused(Action<KScriptPackage.PackageBuilder> register, string package = "KScript.Throwaway")
    {
        var throwaway = new ThrowawayPackage(package, register);
        string message = Assert.Throws<ConventionException>(() => throwaway.Elements).Message;
        // It stays refused, with the same words, however often it is asked.
        Assert.Equal(message, Assert.Throws<ConventionException>(() => throwaway.Commands).Message);
        Assert.StartsWith(package + " registers ", message);
        return message;
    }

    private static void Accepted(Action<KScriptPackage.PackageBuilder> register, string package = "KScript.Throwaway") =>
        Assert.NotNull(new ThrowawayPackage(package, register).Elements);

    private static ElementInfo Element(string name, params A[] arguments) => new ElementInfo(name, "Does something.", ContentKind.Empty, arguments);

    #region Everything that ships loads clean

    [Fact]
    public void Every_package_that_ships_loads_and_passes_every_rule()
    {
        Assert.Equal(PackageRegistry.BuiltIn.Select(p => p.Name).OrderBy(n => n), Packages.Select(p => p.Name).OrderBy(n => n));
        var problems = new List<string>();
        int checkedNames = 0;
        foreach (KScriptPackage package in Packages)
        {
            problems.AddRange(package.Elements.Values.Select(e => Conventions.Element(package.Name, e.Info)));
            problems.AddRange(package.Commands.Values.Select(c => Conventions.Callable(package.Name, c.Info)));
            problems.AddRange(package.Methods.Select(m => Conventions.Callable(package.Name, m.Info)));
            problems.AddRange(package.Fields.Select(f => Conventions.Field(package.Name, f.Info)));
            problems.AddRange(package.Variables.Keys.Select(v => Conventions.Name(package.Name, "variable", v)));
            problems.AddRange(package.EchoFormats.Keys.Select(v => Conventions.Name(package.Name, "echo format", v)));
            problems.AddRange(package.LogProviders.Keys.Select(v => Conventions.Name(package.Name, "log provider", v)));
            checkedNames += package.Elements.Count + package.Commands.Count + package.Methods.Count + package.Fields.Count;
        }
        problems.AddRange(ErrorTypes.BuiltIn.Keys.Select(Conventions.ErrorTypeName));
        problems.AddRange(Conventions.Together(Packages));
        problems.AddRange(Conventions.Tables(Packages));
        problems.RemoveAll(p => p == null);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(checkedNames > 700, $"Only {checkedNames} registrations were checked.");
    }

    [Fact]
    public void Every_allow_list_is_short_and_says_why()
    {
        var c = typeof(KScript.Runtime.Conventions);
        Assert.Equal(3, Conventions.ThingPackages.Count);
        Assert.Equal(new[] { "KScript.Data" }, Conventions.ThingsInside.Keys);
        Assert.Empty(Conventions.NotALengthOfTime);
        Assert.True(Conventions.StatementAndCommand.Count <= 5);
        Assert.True(Conventions.DeliberatePairs.Count <= 20);
        Assert.True(Conventions.ZeroArgumentMethods.Count <= 40);
        Assert.All(Conventions.StatementAndCommand.Values.Concat(Conventions.DeliberatePairs.Values).Concat(Conventions.ZeroArgumentMethods.Values),
            reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    #endregion

    #region Elements

    [Fact]
    public void An_element_with_a_hyphen_is_refused() =>
        Assert.Equal("KScript.Throwaway registers element 'before-each': element names are PascalCase (things) or snake_case (actions), with no hyphen (before_each or BeforeEach).",
            Refused(p => p.Element(Element("before-each"), null)));

    [Fact]
    public void A_PascalCase_element_outside_the_thing_packages_is_refused() =>
        Assert.Contains("registers element 'SendMail': its elements are actions, so they are snake_case (send_mail). PascalCase is for the packages whose elements describe things: KScript.UI, KScript.Http, KScript.Document.",
            Refused(p => p.Element(Element("SendMail"), null), "KScript.Cache"));

    [Fact]
    public void A_snake_case_element_in_a_thing_package_is_refused() =>
        Assert.Equal("KScript.UI registers element 'text_box': its elements describe things, so they are PascalCase (TextBox).",
            Refused(p => p.Element(Element("text_box"), null), "KScript.UI"));

    [Fact]
    public void The_steps_of_a_migration_are_PascalCase_and_nothing_else_in_the_package_is()
    {
        // KScript.Data has no migration steps yet. When it does they are things inside migration { }: CreateTable, AddField.
        Accepted(p =>
        {
            p.Element(Element("migration"), null);
            p.Element(new ElementInfo("CreateTable", "Makes a table.", ContentKind.Statements) { MustBeInside = new[] { "migration" } }, null);
            p.Element(new ElementInfo("AddField", "Adds a column.", ContentKind.Empty) { MustBeInside = new[] { "migration", "CreateTable" } }, null);
        }, "KScript.Data");
        Assert.Equal("KScript.Data registers element 'CreateTable': only the steps inside migration are PascalCase (declare it with MustBeInside); its other elements are actions, in snake_case (create_table).",
            Refused(p => p.Element(Element("CreateTable"), null), "KScript.Data"));
        Assert.Contains("only the steps inside migration are PascalCase", Refused(p => p.Element(new ElementInfo("Seed", "Adds rows.", ContentKind.Statements) { MustBeInside = new[] { "transaction" } }, null), "KScript.Data"));
        Assert.Contains("its elements are actions", Refused(p => p.Element(new ElementInfo("CreateTable", "Makes a table.", ContentKind.Statements) { MustBeInside = new[] { "migration" } }, null), "KScript.Cache"));
    }

    [Fact]
    public void A_retired_element_name_is_refused()
    {
        Assert.Contains("registers element 'marray': that name was retired; it is now collection (Retired.RenamedElements).", Refused(p => p.Element(Element("marray"), null)));
        Assert.Contains("registers element 'write': that name is in Retired.Elements", Refused(p => p.Element(Element("write"), null)));
    }

    [Fact]
    public void An_element_registered_twice_is_refused() =>
        Assert.Equal("KScript.Throwaway registers element 'beep' twice: a package has each element once.",
            Refused(p => { p.Element(Element("beep"), null); p.Element(Element("beep"), null); }));

    #endregion

    #region Arguments

    [Fact]
    public void A_kebab_case_argument_is_refused() =>
        Assert.Equal("KScript.Cache registers argument 'max-age' on 'cache': arguments are snake_case (max_age).",
            Refused(p => p.Element(Element("cache", new A("max-age", K.Expression, "How long to keep it.")), null), "KScript.Cache"));

    [Fact]
    public void A_camelCase_argument_is_refused() =>
        Assert.Contains("argument 'maxTokens' on 'ask': arguments are snake_case (max_tokens).", Refused(p => p.Element(Element("ask", new A("maxTokens", K.Expression, "The most tokens.")), null)));

    [Fact]
    public void To_that_declares_a_variable_is_refused() =>
        Assert.Contains("argument 'to' on 'ask': to is where something goes (a file, a URL, a controller). The variable that receives a result is into.",
            Refused(p => p.Element(Element("ask", new A("to", K.DeclareVariable, "The variable for the answer.")), null)));

    [Fact]
    public void Into_that_is_not_a_variable_is_refused() =>
        Assert.Contains("argument 'into' on 'save': into always names the variable that receives the result",
            Refused(p => p.Element(Element("save", new A("into", K.Path, "The file to save to.")), null)));

    [Fact]
    public void A_result_declared_under_another_name_is_refused()
    {
        Assert.Contains("argument 'result' on 'ask': an argument that declares a variable is called name, into, as, each or at.",
            Refused(p => p.Element(Element("ask", new A("result", K.DeclareVariable, "The variable for the answer.")), null)));
        Assert.Contains("argument 'index' on 'walk': the variable for a position is at, as in for item at i in $list.",
            Refused(p => p.Element(Element("walk", new A("index", K.DeclareVariable, "The position.")), null)));
        Accepted(p => p.Element(Element("walk", new A("each", K.DeclareVariables, "The item."), new A("at", K.DeclareVariable, "The position."), new A("into", K.DeclareVariable, "The result.")), null));
    }

    [Theory]
    [InlineData("src")]
    [InlineData("file")]
    [InlineData("filename")]
    [InlineData("sample")]
    public void A_file_argument_that_is_not_path_is_refused(string name) =>
        Assert.Contains($"argument '{name}' on 'load_it': the file an element reads or acts on is path, and where its result goes is to.",
            Refused(p => p.Element(Element("load_it", new A(name, K.Path, "The file.")), null)));

    [Fact]
    public void A_function_argument_that_does_not_show_the_at_form_is_refused()
    {
        Assert.Contains("argument 'fn' on 'later': an argument that names a function shows the @name form in its description (fn: @send).",
            Refused(p => p.Element(Element("later", new A("fn", K.FunctionReference, "The function to call.")), null)));
        Accepted(p => p.Element(Element("later", new A("fn", K.FunctionReference, "The function to call: `@send`.")), null));
    }

    [Fact]
    public void A_length_of_time_that_is_not_a_duration_is_refused()
    {
        Assert.Contains("argument 'timeout' on 'fetch': a length of time is a duration (30s, 5m, 1h30m). Declare it with Type = Duration.TypeName.",
            Refused(p => p.Element(Element("fetch", new A("timeout", K.Expression, "How long to wait.")), null)));
        Assert.Contains("argument 'retry_after' on 'fetch': a length of time is a duration", Refused(p => p.Element(Element("fetch", new A("retry_after", K.Expression, "How long to wait.")), null)));
        Assert.Contains("parameter 'ttl' of method 'box->keep': a length of time is a duration (30s, 5m, 1h30m). Declare it as ttl: duration.",
            Refused(p => p.Method("box", "keep", "key, ttl?", "Keeps a value.", KType.Any, Nothing)));
        Accepted(p =>
        {
            p.Element(Element("fetch", new A("timeout", K.Expression, "How long to wait.") { Type = Duration.TypeName }), null);
            p.Method("box", "keep", "key, ttl?: duration", "Keeps a value.", KType.Any, Nothing);
        });
    }

    [Fact]
    public void An_argument_named_for_a_unit_of_time_is_refused()
    {
        Assert.Contains("argument 'ms' on 'nap': the unit is part of the value (duration: 500ms), not of the argument's name.",
            Refused(p => p.Element(Element("nap", new A("ms", K.Expression, "How long.")), null)));
        Assert.Contains("parameter 'seconds' of command '@nap': the unit is part of the value", Refused(p => p.Command("nap", "seconds", "Waits.", KType.Any, Nothing)));
        Accepted(p => p.Method("stamp", "add_hours", "hours", "A later time.", "stamp", Nothing));
    }

    [Fact]
    public void A_list_given_as_text_with_commas_is_refused()
    {
        Assert.Contains("argument 'tags' on 'post': a list is written as an array ([a, b]), not as text with commas. Give it a list kind, or NameList = true for a list of names.",
            Refused(p => p.Element(Element("post", new A("tags", K.Text, "The tags, comma separated.")), null)));
        Accepted(p => p.Element(Element("post", new A("tags", K.Text, "The tags, as a list (comma separated in XML).") { NameList = true }, new A("values", K.ExpressionList, "Values, comma separated.")), null));
    }

    [Fact]
    public void A_retired_argument_name_is_refused() =>
        Assert.Contains("argument 'index' on 'for': that name was retired; it is now at (Retired.RenamedAttributes).",
            Refused(p => p.Element(Element("for", new A("index", K.Expression, "The position.")), null)));

    [Fact]
    public void An_argument_listed_twice_is_refused() =>
        Assert.Contains("argument 'path' on 'load_it': it is listed twice.",
            Refused(p => p.Element(Element("load_it", new A("path", K.Path, "The file."), new A("path", K.Path, "The file again.")), null)));

    #endregion

    #region Commands, methods and parameters

    [Fact]
    public void A_command_or_method_that_is_not_snake_case_is_refused()
    {
        Assert.Equal("KScript.Throwaway registers command '@readFile': command and method names are snake_case (read_file).",
            Refused(p => p.Command("readFile", "path", "Reads a file.", KType.String, Nothing)));
        Assert.Equal("KScript.Throwaway registers method 'string->to-upper': command and method names are snake_case (to_upper).",
            Refused(p => p.Method(KType.String, "to-upper", "", "The text in capitals.", KType.String, Nothing)));
        Assert.Equal("KScript.Throwaway registers parameter 'maxAge' of command '@keep': parameter names are snake_case (max_age).",
            Refused(p => p.Command("keep", "key, maxAge?", "Keeps a value.", KType.Any, Nothing)));
    }

    [Fact]
    public void Colour_in_a_name_is_refused()
    {
        Assert.Contains("registers command '@colour_of': names say color", Refused(p => p.Command("colour_of", "name", "Looks a colour up.", KType.Any, Nothing)));
        Assert.Contains("registers parameter 'colour' of command '@paint': names say color", Refused(p => p.Command("paint", "colour", "Paints.", KType.Any, Nothing)));
        Assert.Contains("registers value 'colour' of parameter 'mode' of command '@paint': names say color", Refused(p => p.Command("paint", "mode: colour|mono", "Paints.", KType.Any, Nothing)));
        Assert.Contains("registers argument 'colour' on 'paint': names say color", Refused(p => p.Element(Element("paint", new A("colour", K.Text, "The colour.")), null)));
        Assert.Contains("registers option 'colour' of argument 'mode' on 'paint': names say color", Refused(p => p.Element(Element("paint", new A("mode", K.Enum, "The mode.", false, "colour", "mono")), null)));
        Assert.Contains("registers field 'pen.colour': names say color", Refused(p => p.Field("pen", "colour", KType.String, "The pen's colour.")));
        Assert.Contains("registers variable 'colours': names say color", Refused(p => p.Variable("colours", KType.List, "The colours.", (i, m) => null)));
        Assert.Contains("registers echo format 'colour': names say color", Refused(p => p.EchoFormat("colour", (text, i) => text)));
        Assert.Contains("registers log provider 'colour': names say color", Refused(p => p.LogProvider("colour", "Logs in colour.", c => null)));
    }

    [Fact]
    public void A_variable_that_is_not_snake_case_is_refused() =>
        Assert.Equal("KScript.Throwaway registers variable 'appName': names are snake_case (app_name).", Refused(p => p.Variable("appName", KType.String, "The app's name.", (i, m) => null)));

    [Fact]
    public void A_name_that_answers_true_or_false_without_is_or_has_is_refused()
    {
        Assert.Contains("registers method 'box->empty'", Refused(p => p.Method("box", "empty", "", "True when nothing is in it.", KType.Bool, Nothing)));
        Assert.StartsWith("KScript.Throwaway registers method 'box->open': it answers true or false, so its name starts with is_ or has_ (is_open)",
            Refused(p => p.Method("box", "open", "", "True when it is open.", KType.Bool, Nothing)));
        Assert.StartsWith("KScript.Throwaway registers command '@ready': it answers true or false", Refused(p => p.Command("ready", "", "True when someone is signed in.", KType.Bool, Nothing)));
        // A verb question asks about an argument: without one it isn't a question.
        Assert.StartsWith("KScript.Throwaway registers method 'box->contains': it answers true or false", Refused(p => p.Method("box", "contains", "", "True when it holds something.", KType.Bool, Nothing)));
        Assert.Equal("KScript.Throwaway registers field 'box.open': it is true or false, so its name starts with is_ or has_ (is_open).",
            Refused(p => p.Field("box", "open", KType.Bool, "True when it is open.")));
        Accepted(p =>
        {
            p.Method("box", "is_open", "", "True when it is open.", KType.Bool, Nothing);
            p.Method("box", "contains", "item", "True when the item is in it.", KType.Bool, Nothing);
            p.Method("box", "remove", "item", "Takes the item out and says whether it was there.", KType.Bool, Nothing);
            p.Field("box", "has_lid", KType.Bool, "True when it has a lid.");
        });
    }

    [Fact]
    public void An_is_or_has_name_that_does_not_answer_true_or_false_is_refused()
    {
        Assert.Equal("KScript.Throwaway registers method 'box->is_open': a name that starts with is_ or has_ answers true or false (returns bool).",
            Refused(p => p.Method("box", "is_open", "", "How far it is open.", KType.Number, Nothing)));
        Assert.Equal("KScript.Throwaway registers field 'box.has_lid': a name that starts with is_ or has_ is true or false (type bool).",
            Refused(p => p.Field("box", "has_lid", KType.String, "The lid's name.")));
    }

    [Fact]
    public void A_description_that_says_yes_or_no_is_refused()
    {
        Assert.Contains("registers method 'box->is_open': booleans are true and false. Its description says yes or no",
            Refused(p => p.Method("box", "is_open", "", "True when open: yes if the lid is up.", KType.Bool, Nothing)));
        Assert.Contains("registers argument 'loud' on 'beep': booleans are true and false.", Refused(p => p.Element(Element("beep", new A("loud", K.Bool, "yes for a loud beep.")), null)));
        Assert.Contains("registers method 'box->is_open': describe an is_x or has_x as \"True when ...\"", Refused(p => p.Method("box", "is_open", "", "Whether the lid is up.", KType.Bool, Nothing)));
        Assert.Contains("registers field 'box.is_open': describe a field that is true or false as \"True when ...\"", Refused(p => p.Field("box", "is_open", KType.Bool, "Whether the lid is up.")));
    }

    [Fact]
    public void Count_with_arguments_length_and_a_count_field_are_refused()
    {
        Assert.Equal("KScript.Throwaway registers method 'box->count': ->count() takes no arguments; counting what matches is ->count_of(match).",
            Refused(p => p.Method("box", "count", "match", "How many match.", KType.Number, Nothing)));
        Assert.Equal("KScript.Throwaway registers method 'box->length': the number of items or characters is ->count(), not ->length().",
            Refused(p => p.Method("box", "length", "", "How many items.", KType.Number, Nothing)));
        Assert.Equal("KScript.Throwaway registers field 'box.count': \"how many\" is the method ->count() on every type, never a field.",
            Refused(p => p.Field("box", "count", KType.Number, "How many items.")));
        Assert.Contains("field 'box.length'", Refused(p => p.Field("box", "length", KType.Number, "How many items.")));
    }

    [Fact]
    public void A_field_that_is_not_snake_case_is_refused() =>
        Assert.Equal("KScript.Throwaway registers field 'box.lidColor': field names are snake_case (lid_color).", Refused(p => p.Field("box", "lidColor", KType.String, "The lid's color.")));

    [Fact]
    public void A_second_verb_for_things_kept_by_key_is_refused()
    {
        Assert.Contains("registers method 'box->put': things kept by key use get, set, has and remove, and clear empties them.", Refused(p => p.Method("box", "put", "key, value", "Stores a value.", KType.Any, Nothing)));
        Assert.Contains("registers method 'box->forget'", Refused(p => p.Method("box", "forget", "key", "Takes a value out.", KType.Any, Nothing)));
        Assert.Contains("registers method 'box->flush': flush means \"write out what is being held back\"", Refused(p => p.Method("box", "flush", "", "Empties the box.", KType.Any, Nothing)));
    }

    [Fact]
    public void A_retired_command_or_method_is_refused()
    {
        Assert.Contains("registers command '@signed_in': that command is in Retired.Commands", Refused(p => p.Command("signed_in", "", "Who is signed in.", KType.Any, Nothing)));
        Assert.Contains("registers method 'list->where': that method is in RetiredMembers.Methods", Refused(p => p.Method(KType.List, "where", "fn", "The items that match.", KType.List, Nothing)));
    }

    [Fact]
    public void A_command_described_as_another_name_for_something_is_refused()
    {
        Assert.Contains("registers method 'list->pick': its description says it is another name for something.", Refused(p => p.Method(KType.List, "pick", "fn", "The items that match (the same as filter).", KType.List, Nothing)));
        Assert.Contains("registers command '@new_id': its description says", Refused(p => p.Command("new_id", "", "An alias of @uuid().", KType.String, Nothing)));
    }

    [Fact]
    public void A_command_registered_twice_is_refused() =>
        Assert.Equal("KScript.Throwaway registers command '@beep' twice: a package has each command once.",
            Refused(p => { p.Command("beep", "", "Beeps.", KType.Any, Nothing); p.Command("beep", "times", "Beeps often.", KType.Any, Nothing); }));

    #endregion

    #region Rules about several registrations

    [Fact]
    public void A_method_registered_twice_for_a_type_is_refused() =>
        Assert.Contains("registers method 'box->open_lid': that method is already registered. A type has each method once, from one package.",
            Refused(p => { p.Method("box", "open_lid", "", "Opens it.", KType.Any, Nothing); p.Method("box", "open_lid", "how", "Opens it some way.", KType.Any, Nothing); }));

    [Fact]
    public void A_name_that_is_a_field_and_a_method_is_refused() =>
        Assert.Contains("registers method 'box->size': 'box.size' is a field. A name is a field or a method of a type, never both.",
            Refused(p => { p.Method("box", "size", "unit", "How big it is.", KType.Number, Nothing); p.Field("box", "size", KType.Number, "How big it is."); }));

    [Fact]
    public void A_plain_fact_as_a_method_with_no_arguments_is_refused()
    {
        Assert.Contains("registers method 'box->width': it takes no arguments on a type that has fields, so it is a plain fact: make it a field (p.Field), or add it to Conventions.ZeroArgumentMethods with the work it does.",
            Refused(p => { p.Field("box", "height", KType.Number, "How tall it is."); p.Method("box", "width", "", "How wide it is.", KType.Number, Nothing); }));
        Accepted(p => { p.Field("box", "height", KType.Number, "How tall it is."); p.Method("box", "to_crate", "", "The box as a crate.", KType.Any, Nothing); p.Method("box", "is_open", "", "True when it is open.", KType.Bool, Nothing); });
    }

    [Fact]
    public void Get_and_set_without_has_and_remove_or_with_delete_are_refused()
    {
        Assert.Contains("box has get and set, so it needs has.", Refused(p =>
        {
            p.Method("box", "get", "key", "A value.", KType.Any, Nothing);
            p.Method("box", "set", "key, value", "Stores a value.", KType.Any, Nothing);
            p.Method("box", "remove", "key", "Takes a value out.", KType.Bool, Nothing);
        }));
        Assert.Contains("registers method 'box->delete': box has get and set, so it removes a key with remove, not delete.", Refused(p =>
        {
            p.Method("box", "get", "key", "A value.", KType.Any, Nothing);
            p.Method("box", "set", "key, value", "Stores a value.", KType.Any, Nothing);
            p.Method("box", "has", "key", "True when the key is set.", KType.Bool, Nothing);
            p.Method("box", "remove", "key", "Takes a value out.", KType.Bool, Nothing);
            p.Method("box", "delete", "key", "Deletes a value.", KType.Bool, Nothing);
        }));
    }

    [Fact]
    public void Two_names_with_one_description_are_refused()
    {
        Assert.Contains("registers command '@fresh_id': it has the description of @new_id (\"a new  random id\"). Two names with one description are two spellings of one thing",
            Refused(p => { p.Command("new_id", "", "A new random id.", KType.String, Nothing); p.Command("fresh_id", "", "a new  random id", KType.String, Nothing); }));
        Assert.Contains("registers method 'box->drop': it has the description of box->tip",
            Refused(p => { p.Method("box", "tip", "where", "Empties the box out.", KType.Any, Nothing); p.Method("box", "drop", "where", "Empties the box out.", KType.Any, Nothing); }));
    }

    [Fact]
    public void A_statement_and_a_command_with_one_name_are_refused() =>
        Assert.Contains("registers element 'send_mail': @send_mail() is a command. An action has one form: a command when it takes values and gives a value back, a statement only when it needs a block or prompt text.",
            Refused(p => { p.Command("send_mail", "address, subject", "Sends a message.", KType.Any, Nothing); p.Element(Element("send_mail"), null); }));

    [Fact]
    public void A_command_that_only_repeats_a_method_is_refused()
    {
        Assert.Contains("registers command '@upper': it only repeats the method string->upper. Methods are the primary form",
            Refused(p => { p.Method(KType.String, "upper", "", "The text in capitals.", KType.String, Nothing); p.Command("upper", "text", "Capitals.", KType.String, Nothing); }));
        Assert.Contains("registers command '@upper': it only repeats the method string->to_upper.",
            Refused(p => { p.Method(KType.String, "to_upper", "", "The text in capitals.", KType.String, Nothing); p.Command("upper", "text", "Capitals.", KType.String, Nothing); }));
        Assert.Contains("registers command '@shout': it only repeats the method string->upper.",
            Refused(p => { p.Method(KType.String, "upper", "", "The text in capitals.", KType.String, Nothing); p.Command("shout", "text", "The text in capitals", KType.String, Nothing); }));
    }

    [Fact]
    public void A_deliberate_pair_with_different_parameter_names_is_refused() =>
        // @clamp and number->clamp are a pair on purpose (Conventions.DeliberatePairs), so their names must line up.
        Assert.Contains("registers command '@clamp': a command and the method of the same name (number->clamp) take the same parameter names after the value.",
            Refused(p => { p.Method(KType.Number, "clamp", "min, max", "The number kept between two others.", KType.Number, Nothing); p.Command("clamp", "number, low, high", "Keeps a number in a range.", KType.Number, Nothing); }));

    [Fact]
    public void Two_packages_that_break_a_rule_together_are_refused_when_the_registry_has_loaded_them()
    {
        var registry = new PackageRegistry();
        registry.Register(new ThrowawayPackage("KScript.One", p => p.Method("crate", "tip", "where", "Empties the crate.", KType.Any, Nothing)));
        registry.Register(new ThrowawayPackage("KScript.Two", p => p.Method("crate", "tip", "how", "Tips the crate over.", KType.Any, Nothing)));
        string message = Assert.Throws<ConventionException>(() => registry.LoadAll()).Message;
        Assert.Contains("KScript.Two registers method 'crate->tip': that method is already registered by KScript.One. A type has each method once, from one package.", message);

        // The standard library's own methods count too: a package can't add a second string->upper().
        registry = new PackageRegistry();
        registry.Register(new ThrowawayPackage("KScript.Loud", p => p.Method(KType.String, "upper", "how", "The text, louder.", KType.String, Nothing)));
        Assert.Contains("registers method 'string->upper': that method is already registered by KScript.Core.", Assert.Throws<ConventionException>(() => registry.LoadAll()).Message);

        registry = new PackageRegistry();
        registry.Register(new ThrowawayPackage("KScript.Fine", p => p.Method("crate", "tip", "where", "Empties the crate.", KType.Any, Nothing)));
        Assert.Equal(2, registry.LoadAll().Count());
    }

    #endregion

    #region Error types and settings

    [Fact]
    public void An_error_type_or_a_setting_with_the_wrong_shape_is_refused()
    {
        Assert.Equal("KScript.Core registers error type 'QueryError': no error type but Error itself ends in Error (Query).", Conventions.ErrorTypeName("QueryError"));
        Assert.Equal("KScript.Core registers error type 'not_found': error types are PascalCase (NotFound).", Conventions.ErrorTypeName("not_found"));
        Assert.Null(Conventions.ErrorTypeName("Error"));
        Assert.Equal("kscript.json registers setting 'printWidth' in format: settings are snake_case (print_width).",
            Assert.Throws<ConventionException>(() => Conventions.Settings("format", "indent", "printWidth")).Message);
        Assert.Equal(new[] { "indent", "print_width", "$schema" }, Conventions.Settings("format", "indent", "print_width", "$schema"));
    }

    #endregion

    #region The helpers

    [Theory]
    [InlineData("max-age", "max_age", "MaxAge")]
    [InlineData("maxTokens", "max_tokens", "MaxTokens")]
    [InlineData("HTTPQuery", "http_query", "HTTPQuery")]
    [InlineData("text_box", "text_box", "TextBox")]
    [InlineData("TextBox", "text_box", "TextBox")]
    public void A_message_suggests_the_right_spelling(string name, string snake, string pascal)
    {
        Assert.Equal(snake, Conventions.ToSnakeCase(name));
        Assert.Equal(pascal, Conventions.ToPascalCase(name));
        Assert.True(Conventions.IsSnakeCase(snake));
        Assert.True(Conventions.IsPascalCase(pascal));
    }

    [Theory]
    [InlineData("on_click", true)]
    [InlineData("h1", true)]
    [InlineData("on-click", false)]
    [InlineData("onClick", false)]
    [InlineData("_private", false)]
    [InlineData("1st", false)]
    [InlineData("", false)]
    public void Snake_case_is_letters_digits_and_underscores_starting_with_a_letter(string name, bool snake) =>
        Assert.Equal(snake, Conventions.IsSnakeCase(name));

    #endregion

    /// <summary>A package made for one test: it registers what the test gives it.</summary>
    private sealed class ThrowawayPackage : KScriptPackage
    {
        private readonly Action<PackageBuilder> register;

        public ThrowawayPackage(string name, Action<PackageBuilder> register)
        {
            Name = name;
            this.register = register;
        }

        public override string Name { get; }

        public override string Description => "Registers something for one test.";

        protected override void Register(PackageBuilder package) => register(package);
    }
}
