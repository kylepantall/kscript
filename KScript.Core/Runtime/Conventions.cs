using KScript.Metadata;
using KScript.Syntax.Markup;

namespace KScript.Runtime
{
    /// <summary>
    /// The naming and shape rules of everything a package registers: elements and their arguments, commands, methods
    /// and their parameters, fields, variables and error types. This is the one place the rules live.
    /// <see cref="KScriptPackage.PackageBuilder"/> asks it about every registration and throws a
    /// <see cref="ConventionException"/> for one that breaks a rule, so a package that would bring a second spelling
    /// in doesn't load at all. Rules about several registrations at once (a method that is also a field, a command
    /// that repeats a statement) are checked when a package has finished registering and again when the registry
    /// has loaded every package (<see cref="Together"/>).
    /// <para>
    /// Each check returns null when the registration is fine, or one sentence that names the package, what it
    /// registered and the rule: "KScript.Cache registers argument 'max-age' on 'cache': arguments are snake_case
    /// (max_age).". The checks are plain string comparisons (no reflection, no regular expressions), so they cost
    /// next to nothing when a package loads.
    /// </para>
    /// <para>
    /// Every allow-list is here, short, and says why each name is on it. To add a rule: add a check, call it from
    /// <see cref="Element"/>, <see cref="Callable"/>, <see cref="Field"/> or <see cref="Together"/>, list it in
    /// docs/contributing.md ("The rules the engine enforces") and add a test to ConventionEngineTests that
    /// registers something that breaks it.
    /// </para>
    /// </summary>
    public static class Conventions
    {
        #region The lists

        /// <summary>
        /// The packages whose elements describe a thing in a tree (a window, a route table, a document), so they are
        /// PascalCase. Every other package's elements are actions or declarations, in snake_case. Add a package here
        /// only when its elements build a tree of things.
        /// </summary>
        public static readonly IReadOnlySet<string> ThingPackages = new HashSet<string>(StringComparer.Ordinal) { "KScript.UI", "KScript.Http", "KScript.Document" };

        /// <summary>
        /// Packages whose elements are actions (snake_case) except for one that describes a thing, and the steps
        /// written inside it, which are PascalCase: package, then the element the steps go inside.
        /// KScript.Data: a migration file describes a schema (Migration { CreateTable(...) { Field(...) } }), so
        /// Migration itself and an element registered with MustBeInside = Migration (or inside another step, such as
        /// Field in CreateTable) are PascalCase, while model and transaction stay snake_case.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> ThingsInside = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KScript.Data"] = "Migration",
        };

        /// <summary>The names an argument that declares a variable may have: what is declared (name), a result (into), a caught error or import prefix (as), loop variables (each, at).</summary>
        public static readonly IReadOnlySet<string> DeclaringArguments = new HashSet<string>(StringComparer.Ordinal) { "name", "into", "as", "each", "at" };

        /// <summary>Names an argument for a file must not have: the file an element reads or acts on is path, and where its result goes is to.</summary>
        public static readonly IReadOnlySet<string> FileArgumentNames = new HashSet<string>(StringComparer.Ordinal) { "src", "file", "filename", "file_name", "source", "sample" };

        /// <summary>Names a variable for a position must not have: it is at, as in for item at i in $list.</summary>
        public static readonly IReadOnlySet<string> PositionArgumentNames = new HashSet<string>(StringComparer.Ordinal) { "index", "position", "idx" };

        /// <summary>Verbs that ask a question about their argument, so they read as one without is_: <c>$list->contains(3)</c>.</summary>
        public static readonly IReadOnlySet<string> VerbQuestions = new HashSet<string>(StringComparer.Ordinal)
        {
            "contains", "starts_with", "ends_with", "matches", // text and lists: does it contain, start with, end with, match this?
            "exists",                                          // @exists(path), $storage->exists(name), $query->exists(): is it there?
            "route_is",                                        // @route_is(name or pattern)
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

        /// <summary>The verb questions that may take no arguments: the value itself is what is asked about.</summary>
        private static bool AsksWithoutArguments(string name) => name is "exists" or "needs_rehash" or "not";

        /// <summary>Actions that do something and say whether they did: the name is the action, not a question.</summary>
        public static readonly IReadOnlySet<string> ActionsThatReport = new HashSet<string>(StringComparer.Ordinal)
        {
            "set", "set_if_absent", "remove", "delete", "force_delete", "add", "save", "restore", "retry", "drop_table", "create_table", "revoke_token", "revoke_passkey", "send", "copy", "move",
            "confirm", "assert", "to_bool", "attempt", "login", "logout", "release", "acquire",
        };

        /// <summary>Descriptions that are about a person answering yes or no, not about a boolean in a script.</summary>
        public static readonly IReadOnlySet<string> AboutAPersonsAnswer = new HashSet<string>(StringComparer.Ordinal)
        {
            "confirm", // asks a yes/no question at the terminal
        };

        /// <summary>Verbs that things kept by key don't use: they use get, set, has and remove, and clear empties them.</summary>
        public static readonly IReadOnlySet<string> OtherKeyedVerbs = new HashSet<string>(StringComparer.Ordinal) { "put", "forget", "unset", "erase", "purge", "wipe", "empty", "reset_all" };

        /// <summary>The one method called flush: the logger holds entries back and flush writes them out.</summary>
        public const string TheOneFlush = "logger->flush";

        /// <summary>Argument names that are a unit of time. The unit is part of the value (500ms), not of the name.</summary>
        public static readonly IReadOnlySet<string> UnitNames = new HashSet<string>(StringComparer.Ordinal) { "ms", "milliseconds", "seconds", "secs", "minutes", "hours" };

        /// <summary>Argument names that are a length of time, so the argument is declared as a duration. Names ending _for, _after, _until, _timeout and _ttl are too.</summary>
        public static readonly IReadOnlySet<string> TimeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "timeout", "ttl", "every", "delay", "wait", "backoff", "retry_after", "retry_until", "unique_for", "lockout", "duration", "expires",
        };

        private static readonly string[] TimeEndings = { "_for", "_after", "_until", "_timeout", "_ttl" };

        /// <summary>Arguments with a name that sounds like a length of time but that hold something else, as "element name" or "@command name". Empty on purpose.</summary>
        public static readonly IReadOnlyDictionary<string, string> NotALengthOfTime = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Types whose fields are the user's own (a record's columns, a setting's keys), so their own facts stay methods.</summary>
        public static readonly IReadOnlySet<string> FieldsAreTheUsersOwn = new HashSet<string>(StringComparer.Ordinal) { KType.Record, KType.Map, KType.Any };

        /// <summary>
        /// Methods with no arguments, on types of value that have fields, that are right to stay methods: each one does
        /// work every time it is called, changes something, or gives a new value made from this one. Anything else with
        /// no arguments is a plain fact, and plain facts are fields.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> ZeroArgumentMethods = new Dictionary<string, string>(StringComparer.Ordinal)
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
            ["markdown->html"] = "renders the Markdown as HTML",
            ["markdown->latex"] = "renders the Markdown as LaTeX",
            ["type->schema"] = "builds the type's JSON Schema",
            ["error->chain"] = "walks the causes into a list",
            // Work that reads a file, a database, a queue or the network.
            ["file->read_text"] = "reads the file",
            ["file->read_lines"] = "reads the file",
            ["http_response->json"] = "parses the body",
            ["response->check"] = "throws unless the status is 2xx",
            ["document->pages"] = "lays the document out to count its pages",
            ["document->words"] = "counts the words in the text",
            ["markdown->pages"] = "lays the Markdown out to count its pages",
            ["markdown->words"] = "counts the words in the text",
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

        /// <summary>
        /// The pairs where a command and a method of its first argument's type do the same thing on purpose. Methods are
        /// the primary form, so every entry needs a reason. Keys are "@command = type->method".
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> DeliberatePairs = new Dictionary<string, string>(StringComparer.Ordinal)
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

        /// <summary>
        /// Names that are both a statement and a command on purpose. An action has one form: a command when it takes
        /// values and gives a value back, a statement only when it needs a block or prompt text. Each entry says why
        /// this name has both.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> StatementAndCommand = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The statement's text is the prompt, a template that can run over many lines; @llm(prompt) is for a
            // prompt that is already a value, inside an expression. Both take the same options by the same names.
            ["llm"] = "prompt text against a prompt that is a value",
            // Not two forms of one action: the statement changes the current locale, and @locale() reads it.
            ["locale"] = "the statement sets what the command reads",
            // KScript.Web's page element renders the Markdown written inside it (a block of text); KScript.Markdown's
            // @markdown(text) reads Markdown that is already a value.
            ["markdown"] = "a block of Markdown in a page against Markdown that is a value",
        };

        #endregion

        #region Shapes of names

        /// <summary>True for lower snake_case: letters, digits and _, starting with a letter (max_tokens, on_click).</summary>
        public static bool IsSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] < 'a' || name[0] > 'z') return false;
            foreach (char c in name)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            return true;
        }

        /// <summary>True for PascalCase: letters and digits, starting with a capital (Window, TextBox).</summary>
        public static bool IsPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] < 'A' || name[0] > 'Z') return false;
            foreach (char c in name)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
            }
            return true;
        }

        /// <summary>The snake_case spelling of a name: max-tokens and maxTokens are max_tokens.</summary>
        public static string ToSnakeCase(string name)
        {
            var sb = new System.Text.StringBuilder();
            name ??= string.Empty;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c))
                {
                    // maxTokens and HTTPQuery: a new word starts after a small letter, or before one.
                    bool newWord = i > 0 && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
                    if (newWord && sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        /// <summary>The PascalCase spelling of a name: text_box and text-box are TextBox.</summary>
        public static string ToPascalCase(string name)
        {
            var sb = new System.Text.StringBuilder();
            bool upper = true;
            foreach (char c in name ?? string.Empty)
            {
                if (!char.IsLetterOrDigit(c)) { upper = true; continue; }
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            return sb.ToString();
        }

        /// <summary>True for a name that asks a question: has, is_x or has_x.</summary>
        public static bool IsQuestionName(string name) =>
            name != null && (name == "has" || name.StartsWith("is_", StringComparison.Ordinal) || name.StartsWith("has_", StringComparison.Ordinal));

        /// <summary>True for an argument name that is a length of time (timeout, ttl, retry_after, anything ending _for or _until).</summary>
        public static bool IsTimeName(string name)
        {
            if (name == null) return false;
            if (TimeNames.Contains(name)) return true;
            foreach (string ending in TimeEndings)
            {
                if (name.Length > ending.Length && name.EndsWith(ending, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool Declares(AttributeInfo a) => a.Kind is AttributeKind.DeclareVariable or AttributeKind.DeclareVariables;

        private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>True when <paramref name="word"/> is in the text as a whole word (or, with <paramref name="prefix"/>, starts one).</summary>
        private static bool HasWord(string text, string word, bool prefix = false)
        {
            for (int i = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(word, i + 1, StringComparison.OrdinalIgnoreCase))
            {
                int end = i + word.Length;
                if ((i == 0 || !IsWord(text[i - 1])) && (prefix || end == text.Length || !IsWord(text[end]))) return true;
            }
            return false;
        }

        #endregion

        #region How a problem is said

        private static string Said(string package, string what, string rule) => $"{package ?? "A package"} registers {what}: {rule}";

        private static string Show(ElementInfo e) => $"element '{e.Name}'";

        private static string Show(ElementInfo e, AttributeInfo a) => $"argument '{a.Name}' on '{e.Name}'";

        /// <summary>How a command or method is named in a message: @name or type->name.</summary>
        public static string Key(CallableInfo c) => c.Kind == CallableKind.Method ? c.ReceiverType + "->" + c.Name : "@" + c.Name;

        private static string Show(CallableInfo c) => (c.Kind == CallableKind.Method ? "method '" : "command '") + Key(c) + "'";

        private static string Show(CallableInfo c, ParameterInfo p) => $"parameter '{p.Name}' of {(c.Kind == CallableKind.Method ? "method" : "command")} '{Key(c)}'";

        private static string Show(ValueFieldInfo f) => $"field '{f.Receiver}.{f.Name}'";

        #endregion

        #region Elements and their arguments

        /// <summary>Every rule about one element and its arguments: the first one broken, or null.</summary>
        public static string Element(string package, ElementInfo e)
        {
            string problem = ElementName(package, e) ?? RetiredElement(package, e) ?? (SaysColour(e.Name) ? Color(package, Show(e), e.Name) : null)
                ?? (SaysYesOrNo(e.Description) ? TrueAndFalse(package, Show(e), e.Description, AboutAPersonsAnswer.Contains(e.Name)) : null);
            if (problem != null) return problem;
            for (int i = 0; i < e.Attributes.Count; i++)
            {
                AttributeInfo a = e.Attributes[i];
                problem = ArgumentName(package, e, a) ?? ToIsADestination(package, e, a) ?? IntoIsAResult(package, e, a) ?? PositionArgument(package, e, a)
                    ?? DeclaringArgument(package, e, a) ?? FileArgument(package, e, a) ?? FunctionArgument(package, e, a) ?? LengthOfTimeArgument(package, e, a)
                    ?? UnitArgument(package, e, a) ?? ListArgument(package, e, a) ?? RetiredArgument(package, e, a) ?? ArgumentColor(package, e, a)
                    ?? (SaysYesOrNo(a.Description) ? TrueAndFalse(package, Show(e, a), a.Description) : null);
                if (problem != null) return problem;
                for (int j = 0; j < i; j++)
                {
                    if (e.Attributes[j].Name == a.Name) return Said(package, Show(e, a), "it is listed twice. An element has each argument once.");
                }
            }
            return null;
        }

        /// <summary>
        /// Element names are PascalCase or snake_case with no hyphen: PascalCase for the packages whose elements
        /// describe things (<see cref="ThingPackages"/>, and the steps of <see cref="ThingsInside"/>), snake_case
        /// for every other built-in package. A package that isn't KScript's own may use either.
        /// </summary>
        public static string ElementName(string package, ElementInfo e)
        {
            bool snake = IsSnakeCase(e.Name), pascal = IsPascalCase(e.Name);
            if (!snake && !pascal)
                return Said(package, Show(e), $"element names are PascalCase (things) or snake_case (actions), with no hyphen ({ToSnakeCase(e.Name)} or {ToPascalCase(e.Name)}).");
            if (package == null || !package.StartsWith("KScript.", StringComparison.Ordinal)) return null;
            bool thing = ThingPackages.Contains(package) || (ThingsInside.TryGetValue(package, out string parent) && (e.Name == parent || IsStepInside(e, parent)));
            if (thing && !pascal)
                return Said(package, Show(e), $"its elements describe things, so they are PascalCase ({ToPascalCase(e.Name)}).");
            if (!thing && pascal)
                return Said(package, Show(e), ThingsInside.TryGetValue(package, out string steps)
                    ? $"only {steps} and the steps inside it are PascalCase (declare a step with MustBeInside); its other elements are actions, in snake_case ({ToSnakeCase(e.Name)})."
                    : $"its elements are actions, so they are snake_case ({ToSnakeCase(e.Name)}). PascalCase is for the packages whose elements describe things: {string.Join(", ", ThingPackages)}.");
            return null;
        }

        private static bool IsStepInside(ElementInfo e, string parent)
        {
            if (e.MustBeInside == null || e.MustBeInside.Count == 0) return false;
            foreach (string inside in e.MustBeInside)
            {
                if (inside != parent && !IsPascalCase(inside)) return false;
            }
            return true;
        }

        /// <summary>An element can't take a name that <see cref="Retired"/> tells users was removed or renamed.</summary>
        public static string RetiredElement(string package, ElementInfo e)
        {
            if (Retired.RenamedElements.TryGetValue(e.Name, out RetiredInfo renamed))
                return Said(package, Show(e), $"that name was retired; it is now {renamed.Rename} (Retired.RenamedElements). Don't bring the old name back.");
            if (Retired.Elements.TryGetValue(e.Name, out RetiredInfo retired))
                return Said(package, Show(e), $"that name is in Retired.Elements, which tells users what to write instead ({retired.Replacement}). Don't bring the old name back.");
            return null;
        }

        /// <summary>Argument names are snake_case.</summary>
        public static string ArgumentName(string package, ElementInfo e, AttributeInfo a) =>
            IsSnakeCase(a.Name) ? null : Said(package, Show(e, a), $"arguments are snake_case ({ToSnakeCase(a.Name)}).");

        /// <summary>to is where something goes, never the variable that receives a result.</summary>
        public static string ToIsADestination(string package, ElementInfo e, AttributeInfo a) =>
            a.Name == "to" && Declares(a) ? Said(package, Show(e, a), "to is where something goes (a file, a URL, a controller). The variable that receives a result is into.") : null;

        /// <summary>into is always the variable that receives a result.</summary>
        public static string IntoIsAResult(string package, ElementInfo e, AttributeInfo a) =>
            a.Name == "into" && !Declares(a) ? Said(package, Show(e, a), "into always names the variable that receives the result (K.DeclareVariable). Call a destination to and a page element target.") : null;

        /// <summary>The variable for a position is at.</summary>
        public static string PositionArgument(string package, ElementInfo e, AttributeInfo a) =>
            PositionArgumentNames.Contains(a.Name) && Declares(a) ? Said(package, Show(e, a), "the variable for a position is at, as in for item at i in $list.") : null;

        /// <summary>An argument that declares a variable has one of <see cref="DeclaringArguments"/>.</summary>
        public static string DeclaringArgument(string package, ElementInfo e, AttributeInfo a) =>
            Declares(a) && !DeclaringArguments.Contains(a.Name) ? Said(package, Show(e, a), "an argument that declares a variable is called name, into, as, each or at.") : null;

        /// <summary>The file an element reads or acts on is path, and where its result goes is to.</summary>
        public static string FileArgument(string package, ElementInfo e, AttributeInfo a) =>
            FileArgumentNames.Contains(a.Name) ? Said(package, Show(e, a), "the file an element reads or acts on is path, and where its result goes is to.") : null;

        /// <summary>An argument that names a function shows the @name form in its description.</summary>
        public static string FunctionArgument(string package, ElementInfo e, AttributeInfo a)
        {
            if (!CodeArguments.TakesFunctions(e.Name, a)) return null;
            string d = a.Description ?? string.Empty;
            for (int i = d.IndexOf('@'); i >= 0 && i + 1 < d.Length; i = d.IndexOf('@', i + 1))
            {
                if ((d[i + 1] >= 'a' && d[i + 1] <= 'z') || d[i + 1] == '_') return null;
            }
            return Said(package, Show(e, a), "an argument that names a function shows the @name form in its description (fn: @send).");
        }

        /// <summary>An argument named for a length of time is declared as a duration.</summary>
        public static string LengthOfTimeArgument(string package, ElementInfo e, AttributeInfo a) =>
            IsTimeName(a.Name) && a.Type != Duration.TypeName && !NotALengthOfTime.ContainsKey(e.Name + " " + a.Name)
                ? Said(package, Show(e, a), "a length of time is a duration (30s, 5m, 1h30m). Declare it with Type = Duration.TypeName.") : null;

        /// <summary>No argument is named for a unit of time.</summary>
        public static string UnitArgument(string package, ElementInfo e, AttributeInfo a) =>
            UnitNames.Contains(a.Name) ? Said(package, Show(e, a), "the unit is part of the value (duration: 500ms), not of the argument's name.") : null;

        /// <summary>An argument that holds several things is a list kind, never text documented as separated by commas.</summary>
        public static string ListArgument(string package, ElementInfo e, AttributeInfo a)
        {
            if (CodeArguments.IsList(a.Kind, a) || a.Description == null) return null;
            return a.Description.Contains("comma separated", StringComparison.OrdinalIgnoreCase) || a.Description.Contains("comma-separated", StringComparison.OrdinalIgnoreCase)
                || a.Description.Contains("separated by commas", StringComparison.OrdinalIgnoreCase)
                ? Said(package, Show(e, a), "a list is written as an array ([a, b]), not as text with commas. Give it a list kind, or NameList = true for a list of names.") : null;
        }

        /// <summary>An element can't take back an argument name that <see cref="Retired.RenamedAttributes"/> says was renamed or removed.</summary>
        public static string RetiredArgument(string package, ElementInfo e, AttributeInfo a)
        {
            foreach (var row in Retired.RenamedAttributes)
            {
                if (row.Old == a.Name && row.Element == e.Name)
                    return Said(package, Show(e, a), row.New != null ? $"that name was retired; it is now {row.New} (Retired.RenamedAttributes)." : "that name was retired (Retired.RenamedAttributes).");
            }
            return null;
        }

        /// <summary>Names and options say color.</summary>
        public static string ArgumentColor(string package, ElementInfo e, AttributeInfo a)
        {
            if (SaysColour(a.Name)) return Color(package, Show(e, a), a.Name);
            for (int i = 0; i < a.Options.Count; i++)
            {
                if (SaysColour(a.Options[i])) return Color(package, $"option '{a.Options[i]}' of {Show(e, a)}", a.Options[i]);
            }
            return null;
        }

        #endregion

        #region Commands, methods and their parameters

        /// <summary>Every rule about one command or method and its parameters: the first one broken, or null.</summary>
        public static string Callable(string package, CallableInfo c)
        {
            string problem = CallableName(package, c) ?? (SaysColour(c.Name) ? Color(package, Show(c), c.Name) : null) ?? RetiredCallable(package, c) ?? Count(package, c) ?? AnswersTrueOrFalse(package, c)
                ?? QuestionGivesTrueOrFalse(package, c) ?? KeyedVerb(package, c) ?? Flush(package, c) ?? SecondName(package, c)
                ?? (SaysYesOrNo(c.Description) ? TrueAndFalse(package, Show(c), c.Description) : null) ?? StartsWithTrue(package, c);
            if (problem != null) return problem;
            foreach (Signature s in c.Signatures)
            {
                foreach (ParameterInfo p in s.Parameters)
                {
                    problem = ParameterName(package, c, p) ?? ParameterColor(package, c, p) ?? LengthOfTimeParameter(package, c, p) ?? UnitParameter(package, c, p)
                        ?? (SaysYesOrNo(p.Description) ? TrueAndFalse(package, Show(c, p), p.Description) : null);
                    if (problem != null) return problem;
                }
            }
            return null;
        }

        /// <summary>Command and method names are snake_case.</summary>
        public static string CallableName(string package, CallableInfo c) =>
            IsSnakeCase(c.Name) ? null : Said(package, Show(c), $"command and method names are snake_case ({ToSnakeCase(c.Name)}).");

        /// <summary>Parameter names are snake_case.</summary>
        public static string ParameterName(string package, CallableInfo c, ParameterInfo p) =>
            IsSnakeCase(p.Name) ? null : Said(package, Show(c, p), $"parameter names are snake_case ({ToSnakeCase(p.Name)}).");

        /// <summary>Parameter names and the words a parameter allows say color.</summary>
        public static string ParameterColor(string package, CallableInfo c, ParameterInfo p)
        {
            if (SaysColour(p.Name)) return Color(package, Show(c, p), p.Name);
            for (int i = 0; i < p.Values.Count; i++)
            {
                if (SaysColour(p.Values[i])) return Color(package, $"value '{p.Values[i]}' of {Show(c, p)}", p.Values[i]);
            }
            return null;
        }

        /// <summary>A command or method can't take a name <see cref="Retired.Commands"/> or <see cref="RetiredMembers"/> tells users was removed.</summary>
        public static string RetiredCallable(string package, CallableInfo c)
        {
            if (c.Kind == CallableKind.Method)
                return RetiredMembers.Method(c.ReceiverType, c.Name) != null ? Said(package, Show(c), "that method is in RetiredMembers.Methods, which tells users what to write instead. Don't bring the old name back.") : null;
            // A KScript 1 command whose name a package uses again (@average in KScript.Math) says so in its replacement.
            return Retired.Commands.TryGetValue(c.Name, out RetiredInfo r) && !r.Replacement.Contains("@" + c.Name + "(", StringComparison.Ordinal)
                ? Said(package, Show(c), "that command is in Retired.Commands, which tells users what to write instead. Don't bring the old name back.") : null;
        }

        /// <summary>"How many" is ->count() with no arguments on every type; there is no ->length().</summary>
        public static string Count(string package, CallableInfo c)
        {
            if (c.Kind != CallableKind.Method) return null;
            if (c.Name == "length") return Said(package, Show(c), "the number of items or characters is ->count(), not ->length().");
            if (c.Name == "count" && c.MaxArgs > 0) return Said(package, Show(c), "->count() takes no arguments; counting what matches is ->count_of(match).");
            return null;
        }

        /// <summary>A command or method that answers true or false is named is_x or has_x (or is a verb question or an action that reports).</summary>
        public static string AnswersTrueOrFalse(string package, CallableInfo c) =>
            c.ReturnType == KType.Bool && !IsQuestionName(c.Name)
                && !(VerbQuestions.Contains(c.Name) && (c.MaxArgs > 0 || AsksWithoutArguments(c.Name)))
                && !ActionsThatReport.Contains(c.Name)
                ? Said(package, Show(c), $"it answers true or false, so its name starts with is_ or has_ (is_{c.Name}), unless it is a verb that asks about its argument (Conventions.VerbQuestions) or an action that says whether it worked (Conventions.ActionsThatReport).") : null;

        /// <summary>Nothing named is_x or has_x returns anything but true or false.</summary>
        public static string QuestionGivesTrueOrFalse(string package, CallableInfo c) =>
            IsQuestionName(c.Name) && c.ReturnType != KType.Bool ? Said(package, Show(c), "a name that starts with is_ or has_ answers true or false (returns bool).") : null;

        /// <summary>The description of an is_x or has_x starts with "True".</summary>
        public static string StartsWithTrue(string package, CallableInfo c) =>
            c.ReturnType == KType.Bool && IsQuestionName(c.Name) && c.Description != null && !c.Description.StartsWith("True ", StringComparison.Ordinal)
                ? Said(package, Show(c), "describe an is_x or has_x as \"True when ...\" (or \"True for ...\").") : null;

        /// <summary>Things kept by key use get, set, has and remove, and clear empties them.</summary>
        public static string KeyedVerb(string package, CallableInfo c) =>
            OtherKeyedVerbs.Contains(c.Name) ? Said(package, Show(c), "things kept by key use get, set, has and remove, and clear empties them.") : null;

        /// <summary>flush only ever means "write out what is being held back".</summary>
        public static string Flush(string package, CallableInfo c) =>
            c.Name == "flush" && Key(c) != TheOneFlush ? Said(package, Show(c), "flush means \"write out what is being held back\" (the logger's). To remove everything, the word is clear.") : null;

        /// <summary>A description that calls its command or method another name for something announces a second spelling.</summary>
        public static string SecondName(string package, CallableInfo c)
        {
            string d = c.Description;
            if (string.IsNullOrEmpty(d)) return null;
            return HasWord(d, "the same as") || HasWord(d, "alias") || HasWord(d, "aliases") || HasWord(d, "short for") || HasWord(d, "synonym", prefix: true)
                ? Said(package, Show(c), "its description says it is another name for something. One name per thing: keep one.") : null;
        }

        /// <summary>A parameter named for a length of time is declared as a duration.</summary>
        public static string LengthOfTimeParameter(string package, CallableInfo c, ParameterInfo p) =>
            IsTimeName(p.Name) && p.Type != Duration.TypeName && !NotALengthOfTime.ContainsKey(Key(c) + " " + p.Name)
                ? Said(package, Show(c, p), $"a length of time is a duration (30s, 5m, 1h30m). Declare it as {p.Name}: duration.") : null;

        /// <summary>No parameter is named for a unit of time, except the count of a method named for the unit ($date->add_hours(hours)).</summary>
        public static string UnitParameter(string package, CallableInfo c, ParameterInfo p) =>
            UnitNames.Contains(p.Name) && !c.Name.StartsWith("add_", StringComparison.Ordinal)
                ? Said(package, Show(c, p), "the unit is part of the value (timeout: 500ms), not of the parameter's name.") : null;

        #endregion

        #region Fields

        /// <summary>Every rule about one field: the first one broken, or null.</summary>
        public static string Field(string package, ValueFieldInfo f) =>
            FieldName(package, f) ?? (SaysColour(f.Name) ? Color(package, Show(f), f.Name) : null) ?? CountField(package, f) ?? FieldTrueOrFalse(package, f) ?? QuestionFieldIsTrueOrFalse(package, f)
                ?? (SaysYesOrNo(f.Description) ? TrueAndFalse(package, Show(f), f.Description) : null) ?? FieldStartsWithTrue(package, f);

        /// <summary>Field names are snake_case.</summary>
        public static string FieldName(string package, ValueFieldInfo f) =>
            IsSnakeCase(f.Name) ? null : Said(package, Show(f), $"field names are snake_case ({ToSnakeCase(f.Name)}).");

        /// <summary>"How many" is the method ->count() on every type, never a field.</summary>
        public static string CountField(string package, ValueFieldInfo f) =>
            f.Name is "count" or "length" ? Said(package, Show(f), "\"how many\" is the method ->count() on every type, never a field.") : null;

        /// <summary>A field that is true or false is named is_x or has_x (or exists, the word @exists(path) uses).</summary>
        public static string FieldTrueOrFalse(string package, ValueFieldInfo f) =>
            f.Type == KType.Bool && !IsQuestionName(f.Name) && f.Name != "exists" ? Said(package, Show(f), $"it is true or false, so its name starts with is_ or has_ (is_{f.Name}).") : null;

        /// <summary>A field named is_x or has_x is true or false.</summary>
        public static string QuestionFieldIsTrueOrFalse(string package, ValueFieldInfo f) =>
            IsQuestionName(f.Name) && f.Type != KType.Bool ? Said(package, Show(f), "a name that starts with is_ or has_ is true or false (type bool).") : null;

        /// <summary>The description of a field that is true or false starts with "True".</summary>
        public static string FieldStartsWithTrue(string package, ValueFieldInfo f) =>
            f.Type == KType.Bool && f.Description != null && !f.Description.StartsWith("True ", StringComparison.Ordinal)
                ? Said(package, Show(f), "describe a field that is true or false as \"True when ...\" (or \"True for ...\").") : null;

        #endregion

        #region Rules for every kind of name

        /// <summary>Names say color (as CSS and the color type do); colour is for prose only.</summary>
        public static string Color(string package, string what, string name) =>
            SaysColour(name) ? Said(package, what, "names say color (as CSS and the color type do); colour is for prose only.") : null;

        private static bool SaysColour(string name) => name != null && name.Contains("colour", StringComparison.OrdinalIgnoreCase);

        private static bool SaysYesOrNo(string description) => !string.IsNullOrEmpty(description) && (SaysYes(description) || SaysNoIf(description));

        /// <summary>
        /// Descriptions say true and false, never yes and no: not the word yes (unless it is quoted text a script
        /// reads, 'yes'->to_bool()), and not "no if", "no when" or "no for" at the start of a sentence.
        /// </summary>
        public static string TrueAndFalse(string package, string what, string description, bool aboutAPersonsAnswer = false)
        {
            return !aboutAPersonsAnswer && SaysYesOrNo(description)
                ? Said(package, what, "booleans are true and false. Its description says yes or no: write \"True when ...\", not \"yes if ...\".") : null;
        }

        private static bool SaysYes(string d)
        {
            for (int i = d.IndexOf("yes", StringComparison.OrdinalIgnoreCase); i >= 0; i = d.IndexOf("yes", i + 1, StringComparison.OrdinalIgnoreCase))
            {
                int end = i + 3;
                bool before = i == 0 || !(IsWord(d[i - 1]) || d[i - 1] == '\'' || d[i - 1] == '"');
                bool after = end == d.Length || !(IsWord(d[end]) || d[end] == '\'' || d[end] == '"');
                if (before && after) return true;
            }
            return false;
        }

        private static bool SaysNoIf(string d)
        {
            for (int i = d.IndexOf("no ", StringComparison.Ordinal); i >= 0; i = d.IndexOf("no ", i + 1, StringComparison.Ordinal))
            {
                bool starts = i == 0 || (i >= 2 && d[i - 1] == ' ' && d[i - 2] is '.' or ':' or '(');
                if (!starts) continue;
                foreach (string next in NoWords)
                {
                    int end = i + 3 + next.Length;
                    if (string.CompareOrdinal(d, i + 3, next, 0, next.Length) == 0 && (end >= d.Length || !IsWord(d[end]))) return true;
                }
            }
            return false;
        }

        private static readonly string[] NoWords = { "if", "when", "for" };

        /// <summary>A variable, log provider, echo format or native type a package adds: snake_case, and color.</summary>
        public static string Name(string package, string kind, string name)
        {
            if (SaysColour(name)) return Color(package, $"{kind} '{name}'", name);
            return IsSnakeCase(name) ? null : Said(package, $"{kind} '{name}'", $"names are snake_case ({ToSnakeCase(name)}).");
        }

        /// <summary>An error type's name is PascalCase, and only the root, Error, ends in Error (Query, not QueryError).</summary>
        public static string ErrorTypeName(string name)
        {
            if (!IsPascalCase(name)) return Said("KScript.Core", $"error type '{name}'", $"error types are PascalCase ({ToPascalCase(name)}).");
            return name != ErrorTypes.Root && name.EndsWith("Error", StringComparison.Ordinal)
                ? Said("KScript.Core", $"error type '{name}'", $"no error type but Error itself ends in Error ({name.Substring(0, name.Length - 5)}).") : null;
        }

        /// <summary>True for the name of a setting in kscript.json: snake_case words joined by single underscores (print_width), or $schema.</summary>
        public static bool IsSettingName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int start = name[0] == '$' ? 1 : 0;
            if (start == name.Length || name[start] < 'a' || name[start] > 'z' || name[name.Length - 1] == '_') return false;
            for (int i = start; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_' ? name[i - 1] == '_' : !((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return false;
            }
            return true;
        }

        /// <summary>A setting in kscript.json is snake_case, like the language's own names (warnings_as_errors, never warningsAsErrors).</summary>
        public static string SettingName(string section, string name) =>
            IsSettingName(name) ? null : Said("kscript.json", $"setting '{name}' in {section}", $"settings are snake_case ({ToSnakeCase(name)}).");

        /// <summary>The names of a section's settings, once each has passed <see cref="SettingName"/>: a list of settings is declared through this.</summary>
        public static string[] Settings(string section, params string[] names)
        {
            foreach (string name in names) Enforce(SettingName(section, name));
            return names;
        }

        #endregion

        #region Rules about several registrations together

        /// <summary>
        /// The rules that need more than one registration: checked for one package when it has finished registering,
        /// and for every installed package together when the registry has loaded them all. A method is registered
        /// once for a type; a name is a field or a method of a type, never both; a plain fact is a field, not a method
        /// with no arguments; a type with get and set has has and remove and no delete; no two commands, and no two
        /// methods of a type, share a description; a name is a statement or a command, not both; no command repeats a
        /// method of its first argument, and where one does on purpose they take the same parameter names.
        /// </summary>
        public static IReadOnlyList<string> Together(IReadOnlyList<KScriptPackage> packages)
        {
            List<string> problems = null;
            void Add(string problem) => (problems ??= new List<string>()).Add(problem);

            var methods = new Dictionary<string, CallableInfo>(StringComparer.Ordinal);
            var byReceiver = new Dictionary<string, List<CallableInfo>>(StringComparer.Ordinal);
            var fields = new Dictionary<string, ValueFieldInfo>(StringComparer.Ordinal);
            var typesWithFields = new HashSet<string>(StringComparer.Ordinal);
            var commands = new List<CallableInfo>();
            var commandNames = new Dictionary<string, CallableInfo>(StringComparer.Ordinal);
            var meanings = new Dictionary<string, CallableInfo>(StringComparer.Ordinal);

            foreach (KScriptPackage package in packages)
            {
                foreach (ValueFieldDefinition f in package.Fields)
                {
                    if (!fields.TryAdd(f.Receiver + "." + f.Name, f.Info))
                        Add(Said(package.Name, Show(f.Info), $"that field is already registered{From(fields[f.Receiver + "." + f.Name].Package, package.Name)}. A type has each field once."));
                    typesWithFields.Add(f.Receiver);
                }
            }
            foreach (KScriptPackage package in packages)
            {
                foreach (MethodDefinition m in package.Methods)
                {
                    string key = Key(m.Info);
                    if (!methods.TryAdd(key, m.Info))
                    {
                        Add(Said(package.Name, Show(m.Info), $"that method is already registered{From(methods[key].Package, package.Name)}. A type has each method once, from one package."));
                        continue;
                    }
                    if (!byReceiver.TryGetValue(m.Receiver, out List<CallableInfo> list)) byReceiver[m.Receiver] = list = new List<CallableInfo>();
                    list.Add(m.Info);
                    if (fields.ContainsKey(m.Receiver + "." + m.Name))
                        Add(Said(package.Name, Show(m.Info), $"'{m.Receiver}.{m.Name}' is a field. A name is a field or a method of a type, never both."));
                    else if (m.Info.MaxArgs == 0 && typesWithFields.Contains(m.Receiver) && !FieldsAreTheUsersOwn.Contains(m.Receiver) && !ConvertsOrAsks(m.Name) && !ZeroArgumentMethods.ContainsKey(key))
                        Add(Said(package.Name, Show(m.Info), "it takes no arguments on a type that has fields, so it is a plain fact: make it a field (p.Field), or add it to Conventions.ZeroArgumentMethods with the work it does."));
                    SameMeaning(package.Name, m.Info, m.Receiver, meanings, Add);
                }
                foreach (CommandDefinition c in package.Commands.Values)
                {
                    if (commandNames.TryAdd(c.Name, c.Info)) commands.Add(c.Info);
                    SameMeaning(package.Name, c.Info, "@", meanings, Add);
                }
            }

            // Things kept by key: get and set come with has and remove, and never delete.
            foreach (var type in byReceiver)
            {
                if (!methods.ContainsKey(type.Key + "->get") || !methods.ContainsKey(type.Key + "->set")) continue;
                string owner = methods[type.Key + "->set"].Package;
                if (methods.ContainsKey(type.Key + "->delete")) Add(Said(methods[type.Key + "->delete"].Package, $"method '{type.Key}->delete'", $"{type.Key} has get and set, so it removes a key with remove, not delete."));
                if (!methods.ContainsKey(type.Key + "->has")) Add(Said(owner, $"methods '{type.Key}->get' and '{type.Key}->set'", $"{type.Key} has get and set, so it needs has."));
                if (!methods.ContainsKey(type.Key + "->remove")) Add(Said(owner, $"methods '{type.Key}->get' and '{type.Key}->set'", $"{type.Key} has get and set, so it needs remove."));
            }

            // One form per action: a statement or a command.
            foreach (KScriptPackage package in packages)
            {
                foreach (ElementDefinition e in package.Elements.Values)
                {
                    if (commandNames.TryGetValue(e.Name, out CallableInfo twin) && !StatementAndCommand.ContainsKey(e.Name))
                        Add(Said(package.Name, Show(e.Info), $"@{twin.Name}() is a command{From(twin.Package, package.Name)}. An action has one form: a command when it takes values and gives a value back, a statement only when it needs a block or prompt text."));
                }
            }

            // Methods are the primary form: no command only repeats a method of its first argument.
            foreach (CallableInfo command in commands)
            {
                string type = FirstArgumentType(command);
                if (type == null) continue;
                Repeats(command, type, methods, meanings, Add);
                if (type != KType.Any) Repeats(command, KType.Any, methods, meanings, Add);
            }
            return (IReadOnlyList<string>)problems ?? Array.Empty<string>();
        }

        private static string From(string other, string package) => other == null || other == package ? string.Empty : " by " + other;

        // to_x gives the value in another form, is_x and has_x ask a question: both are methods everywhere.
        private static bool ConvertsOrAsks(string name) =>
            name.StartsWith("to_", StringComparison.Ordinal) || name.StartsWith("as_", StringComparison.Ordinal) || IsQuestionName(name);

        /// <summary>A description without its case, extra spaces or final full stop: two names with one meaning are two spellings.</summary>
        public static string Meaning(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;
            var sb = new System.Text.StringBuilder(description.Length);
            foreach (char c in description)
            {
                if (char.IsWhiteSpace(c)) { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); }
                else sb.Append(char.ToLowerInvariant(c));
            }
            while (sb.Length > 0 && (sb[sb.Length - 1] == ' ' || sb[sb.Length - 1] == '.')) sb.Length--;
            return sb.ToString();
        }

        private static void SameMeaning(string package, CallableInfo c, string group, Dictionary<string, CallableInfo> meanings, Action<string> add)
        {
            string meaning = Meaning(c.Description);
            if (meaning.Length == 0) return;
            string key = group + "\n" + meaning;
            if (!meanings.TryAdd(key, c))
                add(Said(package, Show(c), $"it has the description of {Key(meanings[key])} (\"{c.Description}\"). Two names with one description are two spellings of one thing: keep one, or say how they differ."));
        }

        /// <summary>The type of a command's first argument: the declared one, or what its name says by convention.</summary>
        private static string FirstArgumentType(CallableInfo command)
        {
            if (command.Signatures.Count == 0 || command.Signatures[0].Parameters.Count == 0) return null;
            ParameterInfo first = command.Signatures[0].Parameters[0];
            if (first.Variadic) return first.Name == "values" ? KType.List : null;
            if (first.Type != null) return first.Type == "int" ? KType.Number : first.Type;
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

        /// <summary>"@command = type->method" for a command and a method of its first argument's type that has its name (or its to_ form) or its description.</summary>
        public static string PairKey(CallableInfo command, CallableInfo method) => $"@{command.Name} = {method.ReceiverType}->{method.Name}";

        /// <summary>
        /// The methods of <paramref name="type"/> that the command repeats: the one with its name, with its to_ form
        /// (@upper and ->to_upper, @to_upper and ->upper), or with its description.
        /// </summary>
        private static void Repeats(CallableInfo command, string type, Dictionary<string, CallableInfo> methods, Dictionary<string, CallableInfo> meanings, Action<string> add)
        {
            string name = command.Name;
            methods.TryGetValue(type + "->" + name, out CallableInfo same);
            methods.TryGetValue(type + "->to_" + name, out CallableInfo longer);
            CallableInfo shorter = null, alike = null;
            if (name.Length > 3 && name.StartsWith("to_", StringComparison.Ordinal)) methods.TryGetValue(type + "->" + name.Substring(3), out shorter);
            string meaning = Meaning(command.Description);
            if (meaning.Length > 0) meanings.TryGetValue(type + "\n" + meaning, out alike);
            if (same != null) Repeats(command, same, add);
            if (longer != null) Repeats(command, longer, add);
            if (shorter != null) Repeats(command, shorter, add);
            if (alike != null && alike != same && alike != longer && alike != shorter) Repeats(command, alike, add);
        }

        private static void Repeats(CallableInfo command, CallableInfo method, Action<string> add)
        {
            if (!DeliberatePairs.ContainsKey(PairKey(command, method)))
            {
                add(Said(command.Package, Show(command), $"it only repeats the method {Key(method)}. Methods are the primary form: write $value->{method.Name}() and drop the command, or add \"{PairKey(command, method)}\" to Conventions.DeliberatePairs with a reason."));
            }
            else if (method.Name == command.Name && !ParametersLineUp(command, method))
            {
                add(Said(command.Package, Show(command), $"a command and the method of the same name ({Key(method)}) take the same parameter names after the value."));
            }
        }

        // @clamp(number, min, max) and $n->clamp(min, max): after the value itself, the names line up. Optional
        // parameters at the end may be missing on either side.
        private static bool ParametersLineUp(CallableInfo command, CallableInfo method)
        {
            IReadOnlyList<ParameterInfo> all = command.Signatures[0].Parameters;
            IReadOnlyList<ParameterInfo> own = method.Signatures.Count == 0 ? Array.Empty<ParameterInfo>() : method.Signatures[0].Parameters;
            int rest = all.Count - 1, shared = Math.Min(rest, own.Count);
            for (int i = 0; i < shared; i++)
            {
                if (all[i + 1].Name != own[i].Name) return false;
            }
            for (int i = shared; i < rest; i++)
            {
                if (!all[i + 1].Optional && !all[i + 1].Variadic) return false;
            }
            for (int i = shared; i < own.Count; i++)
            {
                if (!own[i].Optional && !own[i].Variadic) return false;
            }
            return true;
        }

        /// <summary>
        /// The lists in this class and the retired tables against everything that ships: every new name a retired
        /// one points at exists, and no allow-list entry has gone stale. It only means something with every package
        /// installed (a compiled script leaves packages out), so the convention tests run it rather than the runtime.
        /// </summary>
        public static IReadOnlyList<string> Tables(IReadOnlyList<KScriptPackage> packages)
        {
            var problems = new List<string>();
            List<CallableInfo> methods = packages.SelectMany(p => p.Methods).Select(m => m.Info).ToList();
            List<CallableInfo> commands = packages.SelectMany(p => p.Commands.Values).Select(c => c.Info).ToList();
            List<ElementInfo> elements = packages.SelectMany(p => p.Elements.Values).Select(e => e.Info).ToList();
            var fields = new HashSet<string>(packages.SelectMany(p => p.Fields).Select(f => f.Receiver + "." + f.Name), StringComparer.Ordinal);

            foreach (var r in Retired.RenamedElements)
            {
                if (!elements.Any(e => e.Name == r.Value.Rename)) problems.Add($"Retired.RenamedElements: {r.Key} renames to {r.Value.Rename}, which isn't an element.");
            }
            foreach (var row in Retired.RenamedAttributes)
            {
                List<ElementInfo> named = elements.Where(e => e.Name == row.Element).ToList();
                if (named.Count == 0) problems.Add($"Retired.RenamedAttributes: {row.Element} isn't an element.");
                else if (row.New != null && !named.Any(e => e.GetAttribute(row.New) != null)) problems.Add($"Retired.RenamedAttributes: {row.Element} has no {row.New}.");
            }
            // RetiredMembers is keyed "type:name". A rename that starts with a dot is a field (->ok() became .is_ok).
            foreach (var r in RetiredMembers.Methods)
            {
                if (r.Value.Rename == null) continue;
                string type = r.Key.Substring(0, r.Key.IndexOf(':'));
                bool exists = r.Value.Rename.StartsWith('.')
                    ? fields.Contains(type + r.Value.Rename)
                    : methods.Any(m => m.Name == r.Value.Rename && KType.AcceptsMethod(m.ReceiverType, m.Name, type));
                if (!exists) problems.Add($"RetiredMembers.Methods: {r.Key} renames to {r.Value.Rename}, which {type} doesn't have.");
            }
            foreach (RetiredInfo r in Retired.Commands.Values)
            {
                if (r.Rename != null && !commands.Any(c => c.Name == r.Rename)) problems.Add($"Retired.Commands: @{r.Name} renames to @{r.Rename}, which isn't a command.");
            }

            var real = new HashSet<string>(methods.Where(m => m.MaxArgs == 0).Select(Key), StringComparer.Ordinal);
            problems.AddRange(ZeroArgumentMethods.Keys.Where(k => !real.Contains(k)).Select(k => $"Conventions.ZeroArgumentMethods: {k} is no longer a method with no arguments. Take it off the list."));

            // A deliberate pair is stale when the command no longer repeats the method: Together would say so if it
            // did and the pair weren't listed, so list every pair it finds with the allow-list out of the way.
            var pairs = new HashSet<string>(StringComparer.Ordinal);
            var byKey = new Dictionary<string, CallableInfo>(StringComparer.Ordinal);
            var meanings = new Dictionary<string, CallableInfo>(StringComparer.Ordinal);
            foreach (CallableInfo m in methods)
            {
                byKey.TryAdd(Key(m), m);
                string meaning = Meaning(m.Description);
                if (meaning.Length > 0) meanings.TryAdd(m.ReceiverType + "\n" + meaning, m);
            }
            foreach (CallableInfo command in commands)
            {
                string type = FirstArgumentType(command);
                if (type == null) continue;
                foreach (string receiver in type == KType.Any ? new[] { type } : new[] { type, KType.Any })
                {
                    string meaning = Meaning(command.Description);
                    foreach (string key in new[] { receiver + "->" + command.Name, receiver + "->to_" + command.Name, command.Name.StartsWith("to_", StringComparison.Ordinal) ? receiver + "->" + command.Name.Substring(3) : null })
                    {
                        if (key != null && byKey.TryGetValue(key, out CallableInfo named)) pairs.Add(PairKey(command, named));
                    }
                    if (meaning.Length > 0 && meanings.TryGetValue(receiver + "\n" + meaning, out CallableInfo alike)) pairs.Add(PairKey(command, alike));
                }
            }
            problems.AddRange(DeliberatePairs.Keys.Where(k => !pairs.Contains(k)).Select(k => $"Conventions.DeliberatePairs: \"{k}\" no longer matches a command and a method. Remove it."));
            if (DeliberatePairs.Count > 20) problems.Add("Conventions.DeliberatePairs is meant to stay short (20 at most).");

            var names = new HashSet<string>(commands.Select(c => c.Name).Intersect(elements.Select(e => e.Name)), StringComparer.Ordinal);
            problems.AddRange(StatementAndCommand.Keys.Where(k => !names.Contains(k)).Select(k => $"Conventions.StatementAndCommand: {k} is no longer both a statement and a command. Remove it."));
            return problems;
        }

        #endregion

        /// <summary>Throws a <see cref="ConventionException"/> when a check found a problem.</summary>
        internal static void Enforce(string problem)
        {
            if (problem != null) throw new ConventionException(problem);
        }

        /// <summary>Throws a <see cref="ConventionException"/> for the first of the problems, saying how many more there are.</summary>
        internal static void Enforce(IReadOnlyList<string> problems)
        {
            if (problems.Count == 0) return;
            throw new ConventionException(problems.Count == 1 ? problems[0] : $"{problems[0]} (and {problems.Count - 1} more: {string.Join(" ", problems.Skip(1).Take(5))})");
        }
    }
}
